using System;
using System.Collections.Generic;
using ConsoleLib.Console;
using Plaidman.AnEyeForValue.Events;
using Plaidman.AnEyeForValue.Menus;
using Plaidman.AnEyeForValue.Utils;
using XRL.UI;
using XRL.World.Capabilities;

namespace XRL.World.Parts {
	[Serializable]
	public class AEFV_LootFinder : IPlayerPart {
		[NonSerialized]
		private readonly ZonePopup ItemPopup = new();
		[NonSerialized]
		private AEFV_ItemKnowledge ItemKnowledge = null;

		public Guid AbilityGuid;
		public SortType CurrentSortType = PopupUtils.DefaultSortType();
		public PickupType CurrentPickupType = PopupUtils.DefaultPickupType();

		public override void Write(GameObject basis, SerializationWriter writer) {
			writer.Write(AbilityGuid);
			writer.Write((int)CurrentSortType);
			writer.Write((int)CurrentPickupType);
		}

		public override void Read(GameObject basis, SerializationReader reader) {
			if (reader.ModVersions["Plaidman_AnEyeForValue"] == new Version("1.0.0")) {
				base.Read(basis, reader);
				return;
			}

			if (reader.ModVersions["Plaidman_AnEyeForValue"] == new Version("2.0.0")) {
				reader.ReadNamedFields(this, GetType());
				return;
			}

			AbilityGuid = reader.ReadGuid();
			CurrentSortType = (SortType)reader.ReadInt32();
			CurrentPickupType = (PickupType)reader.ReadInt32();
		}

		public override void Attach() {
			ToggleAbility();
			base.Attach();
		}

		public override void Remove() {
			RemoveAbility();
			base.Remove();
		}

		public override void Register(GameObject go, IEventRegistrar registrar) {
			registrar.Register(CommandEvent.ID);
			base.Register(go, registrar);
		}

		public void ToggleAbility() {
			if (Options.GetOption(XMLStrings.AbilityOption) == "Yes") {
				RequireAbility();
			} else {
				RemoveAbility();
			}
		}

		private void RequireAbility() {
			if (AbilityGuid == Guid.Empty) {
				AbilityGuid = ParentObject.AddActivatedAbility(
					Name: "Zone Loot List",
					Command: XMLStrings.ZLLItemListCommand,
					Class: "Skill",
					UITileDefault: Renderable.UITile("an_eye_for_value.png", 'y', 'r'),
					Silent: true);
			}
		}

		private void RemoveAbility() {
			if (AbilityGuid != Guid.Empty) {
				ParentObject.RemoveActivatedAbility(ref AbilityGuid);
			}
		}

		public override bool HandleEvent(CommandEvent e) {
			if (e.Command == XMLStrings.ZLLItemListCommand) {
				ListItems();
			}

			return base.HandleEvent(e);
		}

		public void UninstallParts() {
			The.Game.HandleEvent(new AEFV_UninstallEvent());
			RemoveAbility();
			ParentObject.RemovePart<AEFV_LootFinder>();
		}

		private AEFV_ItemKnowledge GetItemKnowledge() {
			ItemKnowledge ??= ParentObject.GetPart<AEFV_ItemKnowledge>();
			return ItemKnowledge;
		}

		private void ListItems() {
			ZoneLootUtils.FilterZoneItems(
				ParentObject.CurrentZone.YieldObjects(),
				out List<GameObject> takeableItems,
				out List<GameObject> liquids,
				out List<GameObject> chestItems
			);

			if (liquids.Count == 0 && takeableItems.Count == 0) {
				Popup.Show("You haven't seen any new loot in this area.");
				return;
			}

			var initialSelections = new List<int>();
			for (int i = 0; i < takeableItems.Count; i++) {
				if (takeableItems[i].HasPart<AEFV_AutoGetBeacon>()) {
					initialSelections.Add(i);
				}
			}

			var itemCount = takeableItems.Count + liquids.Count;
			var invList = new List<InventoryItem>(itemCount);
			var goList = new GameObject[itemCount];
			var valueMult = ValueUtils.GetValueMultiplier();

			for (var i = 0; i < takeableItems.Count; i++) {
				var go = takeableItems[i];
				var known = GetItemKnowledge().IsItemKnown(go);
				var inv = new InventoryItem(i, go, valueMult, known, ItemType.Takeable);

				goList[i] = go;
				invList.Add(inv);
			}

			for (var i = 0; i < liquids.Count; i++) {
				var iAdj = i + takeableItems.Count;
				var go = liquids[i];
				var known = GetItemKnowledge().IsLiquidKnown(go.LiquidVolume);
				var inv = new InventoryItem(iAdj, go, valueMult, known, ItemType.Liquid);

				goList[iAdj] = go;
				invList.Add(inv);
			}

			ItemPopup.CurrentSortType = CurrentSortType;
			ItemPopup.CurrentPickupType = CurrentPickupType;
			var toggledItemsEnumerator = ItemPopup.ShowPopup(
				invList,
				initialSelections.ToArray()
			);

			GameObject item;
			foreach (ZonePopupAction result in toggledItemsEnumerator) {
				switch (result.Action) {
					case ActionType.TurnOn:
						item = takeableItems[result.Index];
						item.RemoveIntProperty("AutoexploreActionAutoget");
						item.RequirePart<AEFV_AutoGetBeacon>();
						break;

					case ActionType.TurnOff:
						item = takeableItems[result.Index];
						item.RemovePart<AEFV_AutoGetBeacon>();
						break;

					case ActionType.Sort:
						CurrentSortType = ItemPopup.CurrentSortType;
						CurrentPickupType = ItemPopup.CurrentPickupType;
						break;

					case ActionType.Travel:
						var playerCell = ParentObject.GetCurrentCell();
						var itemCell = goList[result.Index].CurrentCell;
						var landingCell = itemCell.GetCellFromDirectionOfCell(playerCell);

						AutoAct.Setting = "M" + landingCell.X.ToString() + "," + landingCell.Y.ToString();
						The.ActionManager.SkipPlayerTurn = true;
						break;
				}
			}
		}
	}
}
