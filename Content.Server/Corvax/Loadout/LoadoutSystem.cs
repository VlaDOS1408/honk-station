﻿using System.Linq;
using Content.Server.Corvax.Sponsors;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Clothing.Components;
using Content.Shared.Corvax.Loadout;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server.Corvax.Loadout;

public sealed class LoadoutSystem : EntitySystem
{
    private const string BackpackSlotId = "back";

    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly InventorySystem _inventorySystem = default!;
    [Dependency] private readonly HandsSystem _handsSystem = default!;
    [Dependency] private readonly StorageSystem _storageSystem = default!;
    [Dependency] private readonly SponsorsManager _sponsorsManager = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent ev)
    {
        _sponsorsManager.TryGetInfo(ev.Player.UserId, out var sponsor);

        foreach (var loadoutId in ev.Profile.LoadoutPreferences)
        {
            if (!_prototypeManager.TryIndex<LoadoutPrototype>(loadoutId, out var loadout))
                continue;
            var isSponsorOnly = loadout.SponsorOnly && sponsor != null &&
                                !sponsor.AllowedMarkings.Contains(loadoutId);
            var isWhitelisted = ev.JobId == null ||
                                loadout.WhitelistJobs != null &&
                                !loadout.WhitelistJobs.Contains(ev.JobId);
            var isBlacklisted = ev.JobId != null &&
                                loadout.BlacklistJobs != null &&
                                loadout.BlacklistJobs.Contains(ev.JobId);
            var isSpeciesRestricted = loadout.SpeciesRestrictions != null &&
                                      loadout.SpeciesRestrictions.Contains(ev.Profile.Species);

            if (isSponsorOnly || isWhitelisted || isBlacklisted || isSpeciesRestricted)
                continue;

            var entity = Spawn(loadout.Prototype, Transform(ev.Mob).Coordinates);

            // Take in hand if not clothes
            if (!TryComp<ClothingComponent>(entity, out var clothing))
            {
                _handsSystem.TryPickup(ev.Mob, entity);
                continue;
            }

            // Automatically search empty slot for clothes to equip
            string? firstSlotName = null;
            var isEquipped = false;
            foreach (var slot in _inventorySystem.GetSlots(ev.Mob))
            {
                if (!clothing.Slots.HasFlag(slot.SlotFlags))
                    continue;

                firstSlotName ??= slot.Name;

                if (_inventorySystem.TryGetSlotEntity(ev.Mob, slot.Name, out var _))
                    continue;

                if (loadout.Exclusive && _inventorySystem.TryUnequip(ev.Mob, firstSlotName, out var removedItem, true, true))
                    _entityManager.DeleteEntity(removedItem.Value);

                if (!_inventorySystem.TryEquip(ev.Mob, entity, slot.Name, true, loadout.Exclusive))
                    continue;

                isEquipped = true;
                break;
            }

            if (isEquipped || firstSlotName == null)
                continue;

            // Force equip to first valid clothes slot
            // Get occupied entity -> Insert to backpack -> Equip loadout entity
            if (_inventorySystem.TryGetSlotEntity(ev.Mob, firstSlotName, out var slotEntity) &&
                _inventorySystem.TryGetSlotEntity(ev.Mob, BackpackSlotId, out var backEntity) &&
                _storageSystem.CanInsert(backEntity.Value, slotEntity.Value, out _))
            {
                _storageSystem.Insert(backEntity.Value, slotEntity.Value);
            }

            _inventorySystem.TryEquip(ev.Mob, entity, firstSlotName, true);
        }
    }
}
