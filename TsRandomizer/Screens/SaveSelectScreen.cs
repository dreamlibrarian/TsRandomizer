﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SDL2;
using Timespinner.GameAbstractions.Saving;
using Timespinner.GameStateManagement;
using Timespinner.GameStateManagement.ScreenManager;
using TsRandomizer.Drawables;
using TsRandomizer.Extensions;
using TsRandomizer.IntermediateObjects;
using TsRandomizer.Randomisation;
using TsRandomizer.Screens.Menu;

namespace TsRandomizer.Screens
{
	[TimeSpinnerType("Timespinner.GameStateManagement.Screens.MainMenu.SaveSelectScreen")]
	// ReSharper disable once UnusedMember.Global
	class SaveSelectScreen : Screen
	{
		static readonly Type MessageBoxScreenType = TimeSpinnerType.Get("Timespinner.GameStateManagement.MessageBoxScreen");

		const int NumberOfSaveFileSlots = 8;

		readonly Dictionary<object, SeedRepresentation> seedRepresentations = new Dictionary<object, SeedRepresentation>(10);
		readonly Dictionary<object, ArchipelagoRepresentation> archipelagoRepresentations = new Dictionary<object, ArchipelagoRepresentation>(10);

		int fileToDeleteIndex = -1;
		int zoom;

		public SaveSelectScreen(ScreenManager screenManager, GameScreen screen) : base(screenManager, screen)
		{
			var saveFileEntries = (IList)((object)Dynamic._saveFileCollection).AsDynamic().Entries;

			foreach (var entry in saveFileEntries)
			{
				var entryReflected = entry.AsDynamic();

				if (entryReflected.IsEmptySaveSlot)
					continue;

				var saveFile = (GameSave)entryReflected._saveFile;
				var seed = saveFile.GetSeed();

				seedRepresentations.Add(entry, new SeedRepresentation(seed, screenManager.Dynamic.GCM, false));
				archipelagoRepresentations.Add(entry, new ArchipelagoRepresentation(saveFile, screenManager.Dynamic.GCM));
			}
		}

		public override void Update(GameTime gameTime, InputState input)
		{
			if (fileToDeleteIndex >= 0)
				HandleFileDeletion();
			else
				HandleUpdate(input);
		}

		void HandleUpdate(InputState input)
		{
			var saveFileEntries = (IList)((object)Dynamic._saveFileCollection).AsDynamic().Entries;

			if (IsZoomChanged())
			{
				UpdateSeedRepresentationIconSize(saveFileEntries);
				UpdateAreaNameSize(saveFileEntries);
			}

			UpdateDrawPositions(saveFileEntries);

			var missingSeedEntries = new List<object>();

			foreach (var seedRepresentation in seedRepresentations)
			{
				seedRepresentation.Value.ShowSeedId = false;

				if (!saveFileEntries.Contains(seedRepresentation.Key))
					missingSeedEntries.Add(seedRepresentation.Key);
			}

			foreach (var missingEntry in missingSeedEntries)
				seedRepresentations.Remove(missingEntry);

			var missingArchipelagoEntries = new List<object>();
			
			foreach (var archipelagoRepresentation in archipelagoRepresentations)
			{
				archipelagoRepresentation.Value.ShowArchipelagoInfo = false;

				if (!saveFileEntries.Contains(archipelagoRepresentation.Key))
					missingArchipelagoEntries.Add(archipelagoRepresentation.Key);
			}

			foreach (var missingEntry in missingArchipelagoEntries)
				archipelagoRepresentations.Remove(missingEntry);


			UpdateInput(input);
		}

		void HandleFileDeletion()
		{
			var saveFileCollection = ((object) Dynamic._saveFileCollection).AsDynamic();
			var saveFileManager = ((object) Dynamic._saveFileManager).AsDynamic();

			if (!saveFileManager.IsFinishedSaving)
				return;

			var saveFile = GetNextSaveFile(saveFileCollection);
			if (saveFile == null)
			{
				StopDeletion(saveFileCollection);
				return;
			}

			saveFileCollection.SelectedIndex = fileToDeleteIndex;

			saveFileManager.RequestGameSaveDelete(saveFile);
			saveFileCollection.DeleteSelectedFile();
		}

		GameSave GetNextSaveFile(dynamic saveFileCollection)
		{
			while (fileToDeleteIndex < NumberOfSaveFileSlots)
			{
				var saveFile = ((IList) saveFileCollection.SaveFiles)[fileToDeleteIndex].AsDynamic().SaveFile;

				if (saveFile != null)
					return saveFile;

				fileToDeleteIndex++;
			}

			return null;
		}

		void StopDeletion(dynamic saveFileCollection)
		{
			fileToDeleteIndex = -1;
			saveFileCollection.SelectedIndex = 0;
		}

		void UpdateDrawPositions(IList saveFileEntries)
		{
			foreach (var saveFileEntry in saveFileEntries)
			{
				if (!seedRepresentations.ContainsKey(saveFileEntry) && !archipelagoRepresentations.ContainsKey(saveFileEntry)) continue;
				
				var entry = saveFileEntry.AsDynamic();

				if (entry.IsEmptySaveSlot || entry.IsCorrupt)
					continue;

				var drawPosition = (Vector2)entry.DrawPosition;
				var textXOffset = (int)entry._textOffsetX;
				var font = (SpriteFont)entry._font;
				var origin = new Vector2(0.0f, font.LineSpacing / 2f);

				if (seedRepresentations.ContainsKey(saveFileEntry))
				{
					var drawPoint = new Point(
						(int)(drawPosition.X + textXOffset + entry._saveColumnOffsetX - seedRepresentations[saveFileEntry].Width),
						(int)drawPosition.Y);

					seedRepresentations[saveFileEntry].SetDrawPoint(drawPoint, origin);
				}
				if (archipelagoRepresentations.ContainsKey(saveFileEntry))
				{
					var drawPoint = new Point(
						(int)(drawPosition.X + textXOffset),
						(int)drawPosition.Y);

					archipelagoRepresentations[saveFileEntry].SetDrawPoint(drawPoint, origin);
				}
			}
		}

		object CurrentSelectedMenuEntry =>
			seedRepresentations
				.FirstOrDefault(sr => ((GameSave)sr.Key.AsDynamic().SaveFile).SaveFileIndex == Dynamic.SelectedIndex)
				.Key;

		GameSave CurrentSelectedSave => CurrentSelectedMenuEntry?.AsDynamic().SaveFile;

		void UpdateInput(InputState input)
		{
			if (input.IsButtonHold(Buttons.LeftTrigger))
			{
				UpdateDescription(true);

				DisplaySeedId();

				DisplayArchipelagoInfo();

				if (input.IsNewButtonPress(Buttons.X))
				{
					Dynamic._isDeleting = false;

					ShowDeleteAllDialog();
				}
			}
			else
			{
				UpdateDescription(false);
			}

			if (input.IsNewButtonPress(Buttons.RightTrigger))
			{
				var selectedSaveFile = CurrentSelectedSave;
				if(selectedSaveFile == null)
					return;

				ShowSpoilerGenerationDialog(selectedSaveFile);
			}

			if (input.IsControllHold() && input.IsKeyHold(Keys.C))
			{
				var seed = CurrentSelectedSave?.GetSeed();

				if (!seed.HasValue)
					return;

				SDL.SDL_SetClipboardText(seed.Value.ToString());
			}

			if (input.IsNewPressTertiary(null))
			{
				var selectedSaveFile = CurrentSelectedSave;
				if (selectedSaveFile == null)
					return;

				//if save is cleared, cancel newgame+ menu
				if (selectedSaveFile.IsGameCleared)
					ScreenManager.RemoveScreen(ScreenManager.FirstOrDefaultTimespinnerOfType(MessageBoxScreenType));

				var seed = selectedSaveFile.GetSeed();
				if (seed.HasValue && seed.Value.Options.Archipelago)
				{
					//TODO: Implement, change credentials
				}
			}
 		}

		void UpdateDescription(bool displayDeleteAll)
		{
			if (CurrentSelectedMenuEntry == null) return;

			var menuEntry = CurrentSelectedMenuEntry?.AsDynamic();
			if (menuEntry == null) return;

			string desc = TimeSpinnerGame.Localizer.Get("SaveSelectContinueDescription");

			if (displayDeleteAll)
			{
				var xStringStart = desc.IndexOf("$X", StringComparison.InvariantCulture);

				var aString = desc.Substring(0, xStringStart + 3);

				desc = aString + "to delete all files.";
			}

			var seed = CurrentSelectedSave?.GetSeed();
			if (seed.HasValue && seed.Value.Options.Archipelago)
				desc += " Press $Y to change archipelago server/credentials";
			
			menuEntry.Description = desc;

			Dynamic.OnSelectedEntryChanged(Dynamic.SelectedIndex);
		}

		void DisplaySeedId()
		{
			var currentEntry = CurrentSelectedMenuEntry;
			if (currentEntry != null 
			   && seedRepresentations.TryGetValue(currentEntry, out var seedRepresentation) 
			   && seedRepresentation != null)
					seedRepresentation.ShowSeedId = true;
		}

		void DisplayArchipelagoInfo()
		{
			var currentEntry = CurrentSelectedMenuEntry;
			if (currentEntry != null
			   && archipelagoRepresentations.TryGetValue(currentEntry, out var archipelagoRepresentation)
			   && archipelagoRepresentation != null)
				archipelagoRepresentation.ShowArchipelagoInfo = true;
		}

		void ShowDeleteAllDialog()
		{
			var messageBox = MessageBox.Create(ScreenManager, "Delete all saves?", _ => OnDeleteAllSavesAccepted());

			ScreenManager.AddScreen(messageBox.Screen, GameScreen.ControllingPlayer);
		}

		void OnDeleteAllSavesAccepted() => fileToDeleteIndex = 0;

		void ShowSpoilerGenerationDialog(GameSave save)
		{
			var seed = save.GetSeed();

			if (!seed.HasValue)
				return;

			var messageBox = MessageBox.Create(ScreenManager, "Generate Spoiler log?", _ => OnSpoilerLogCreationAccepted(save));

			ScreenManager.AddScreen(messageBox.Screen, GameScreen.ControllingPlayer);
		}

		void OnSpoilerLogCreationAccepted(GameSave save)
		{
			var fillingMethod = save.GetFillingMethod();

			if (fillingMethod == FillingMethod.Archipelago)
			{
				var messageBox = MessageBox.Create(ScreenManager, "Not supported for Archipelago based seed");

				ScreenManager.AddScreen(messageBox.Screen, GameScreen.ControllingPlayer);

				return;
			}

			var seed = save.GetSeed();

			if (!seed.HasValue)
				return;

			using (var file = new StreamWriter(GetFileName(seed.Value)))
			{
				file.WriteLine($"Seed: {seed.Value}");
				file.WriteLine($"Timespinner version: v{TimeSpinnerGame.Constants.GameVersion}");
				file.WriteLine($"TsRandomizer version: v{Assembly.GetExecutingAssembly().GetName().Version}");

				var itemLocations = Randomizer.Randomize(seed.Value, fillingMethod, save);

				var progressionItems = itemLocations.Where(l => l.ItemInfo.Unlocks != Requirement.None);
				var otherItems = itemLocations.Where(l => l.ItemInfo.Unlocks == Requirement.None);

				WriteProgressionChain(file, itemLocations);
				WriteItemListSection(file, progressionItems, "Progression Items:");
				WriteItemListSection(file, otherItems, "Other Locations:");
			}
		}

		static void WriteProgressionChain(StreamWriter file, ItemLocationMap itemLocations)
		{
			file.WriteLine();
			file.WriteLine("Progression Chain:");
			
			var depth = 0;
			var progressionChain = itemLocations.GetProgressionChain();

			do
			{
				WriteItemList(file, progressionChain.Locations, depth);

				depth++;
				progressionChain = progressionChain.Sub;
			} while (progressionChain != null);
		}

		static void WriteItemListSection(StreamWriter file, IEnumerable<ItemLocation> itemLocations, string sectionName)
		{
			file.WriteLine();
			file.WriteLine(sectionName);

			WriteItemList(file, itemLocations, 0);
		}

		static void WriteItemList(StreamWriter file, IEnumerable<ItemLocation> itemLocations, int depth)
		{
			var prefix = new string('\t', depth);

			foreach (var itemLocation in itemLocations)
				file.WriteLine(prefix + itemLocation);
		}

		static string GetFileName(Seed seed)
		{
			var directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
			var fileDateTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH.mm");

			// ReSharper disable once AssignNullToNotNullAttribute
			return Path.Combine(directory, $"SpoilerLog {seed} {fileDateTime}.txt");
		}

		bool IsZoomChanged()
		{
			var newZoom = (int)TimeSpinnerGame.Constants.InGameZoom;

			if (zoom == newZoom) return false;

			zoom = newZoom;

			return true;
		}

		void UpdateSeedRepresentationIconSize(IList saveFileEntries)
		{
			foreach (var saveFileEntry in saveFileEntries)
			{
				if(!seedRepresentations.ContainsKey(saveFileEntry)) continue;

				var entry = saveFileEntry.AsDynamic();
				var font = (SpriteFont)entry._font;

				seedRepresentations[saveFileEntry].IconSize = (int)(font.LineSpacing * zoom * 0.75);
			}
		}

		void UpdateAreaNameSize(IList saveFileEntries)
		{
			foreach (var saveFileEntry in saveFileEntries)
			{
				if (!seedRepresentations.ContainsKey(saveFileEntry)) continue;

				var entry = saveFileEntry.AsDynamic();
				var scrollableTextBlock = ((object)entry._areaNameTextBlock).AsDynamic();

				scrollableTextBlock._baseWidth = 
					entry._leftColumnWidth - ((int)seedRepresentations[saveFileEntry].Width / zoom);

				scrollableTextBlock.MeasureString();
			}
		}

		public override void Draw(SpriteBatch spriteBatch, SpriteFont menuFont)
		{
			if (!GameScreen.IsActive)
				return;

			using (spriteBatch.BeginUsing(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp))
			{
				foreach (var seedRepresentation in seedRepresentations)
					if (!seedRepresentation.Key.AsDynamic().IsScrolledOff)
						seedRepresentation.Value.Draw(spriteBatch);

				foreach (var archipelagoRepresentation in archipelagoRepresentations)
					if (!archipelagoRepresentation.Key.AsDynamic().IsScrolledOff)
						archipelagoRepresentation.Value.Draw(spriteBatch);
			}
		}
	}
}