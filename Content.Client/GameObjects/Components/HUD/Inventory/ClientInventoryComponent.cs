﻿// Only unused on .NET Core due to KeyValuePair.Deconstruct
// ReSharper disable once RedundantUsingDirective
using Robust.Shared.Utility;
using System.Collections.Generic;
using System.Linq;
using Content.Client.GameObjects.Components.Clothing;
using Content.Shared.GameObjects;
using Content.Shared.Preferences.Appearance;
using Robust.Client.GameObjects;
using Robust.Client.Interfaces.GameObjects.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.IoC;
using Robust.Shared.ViewVariables;
using static Content.Shared.GameObjects.Components.Inventory.EquipmentSlotDefines;
using static Content.Shared.GameObjects.SharedInventoryComponent.ClientInventoryMessage;

namespace Content.Client.GameObjects
{
    /// <summary>
    /// A character UI which shows items the user has equipped within his inventory
    /// </summary>
    [RegisterComponent]
    public class ClientInventoryComponent : SharedInventoryComponent
    {
        private readonly Dictionary<Slots, IEntity> _slots = new Dictionary<Slots, IEntity>();

        [ViewVariables]
        public InventoryInterfaceController InterfaceController { get; private set; }

        private ISpriteComponent _sprite;

        private bool _playerAttached = false;

        public override void OnRemove()
        {
            base.OnRemove();

            if (_playerAttached)
            {
                InterfaceController?.PlayerDetached();
            }
            InterfaceController?.Dispose();
        }

        public override void Initialize()
        {
            base.Initialize();

            var controllerType = ReflectionManager.LooseGetType(InventoryInstance.InterfaceControllerTypeName);
            var args = new object[] {this};
            InterfaceController = DynamicTypeFactory.CreateInstance<InventoryInterfaceController>(controllerType, args);
            InterfaceController.Initialize();

            if (Owner.TryGetComponent(out _sprite))
            {
                foreach (var mask in InventoryInstance.SlotMasks.OrderBy(s => InventoryInstance.SlotDrawingOrder(s)))
                {
                    if (mask == Slots.NONE)
                    {
                        continue;
                    }

                    _sprite.LayerMapReserveBlank(mask);
                }
            }

            // Component state already came in but we couldn't set anything visually because, well, we didn't initialize yet.
            foreach (var (slot, entity) in _slots)
            {
                _setSlot(slot, entity);
            }
        }

        public override void HandleComponentState(ComponentState curState, ComponentState nextState)
        {
            base.HandleComponentState(curState, nextState);

            if (curState == null)
                return;

            var cast = (InventoryComponentState) curState;

            var doneSlots = new HashSet<Slots>();

            foreach (var (slot, entityUid) in cast.Entities)
            {
                var entity = Owner.EntityManager.GetEntity(entityUid);
                if (!_slots.ContainsKey(slot) || _slots[slot] != entity)
                {
                    _slots[slot] = entity;
                    _setSlot(slot, entity);
                }
                doneSlots.Add(slot);
            }

            foreach (var slot in _slots.Keys.ToList())
            {
                if (!doneSlots.Contains(slot))
                {
                    _clearSlot(slot);
                    _slots.Remove(slot);
                }
            }
        }

        private void _setSlot(Slots slot, IEntity entity)
        {
            SetSlotVisuals(slot, entity);

            InterfaceController?.AddToSlot(slot, entity);
        }

        internal void SetSlotVisuals(Slots slot, IEntity entity)
        {
            if (_sprite == null)
            {
                return;
            }

            if (entity != null && entity.TryGetComponent(out ClothingComponent clothing))
            {
                var flag = SlotMasks[slot];
                var data = clothing.GetEquippedStateInfo(flag);
                if (data != null)
                {
                    var (rsi, state) = data.Value;
                    _sprite.LayerSetVisible(slot, true);
                    _sprite.LayerSetRSI(slot, rsi);
                    _sprite.LayerSetState(slot, state);

                    if (slot == Slots.INNERCLOTHING)
                    {
                        _sprite.LayerSetState(HumanoidVisualLayers.StencilMask, clothing.FemaleMask switch
                        {
                            FemaleClothingMask.NoMask => "female_none",
                            FemaleClothingMask.UniformTop => "female_top",
                            _ => "female_full",
                        });
                    }

                    return;
                }
            }

            _sprite.LayerSetVisible(slot, false);
        }

        internal void ClearAllSlotVisuals()
        {
            foreach (var slot in InventoryInstance.SlotMasks)
            {
                if (slot != Slots.NONE)
                {
                    _sprite.LayerSetVisible(slot, false);
                }
            }
        }

        private void _clearSlot(Slots slot)
        {
            InterfaceController?.RemoveFromSlot(slot);
            _sprite?.LayerSetVisible(slot, false);
        }

        public void SendEquipMessage(Slots slot)
        {
            var equipmessage = new ClientInventoryMessage(slot, ClientInventoryUpdate.Equip);
            SendNetworkMessage(equipmessage);
        }

        public void SendUseMessage(Slots slot)
        {
            var equipmessage = new ClientInventoryMessage(slot, ClientInventoryUpdate.Use);
            SendNetworkMessage(equipmessage);
        }

        public void SendOpenStorageUIMessage(Slots slot)
        {
            SendNetworkMessage(new OpenSlotStorageUIMessage(slot));
        }

        public override void HandleMessage(ComponentMessage message, INetChannel netChannel = null,
            IComponent component = null)
        {
            base.HandleMessage(message, netChannel, component);

            switch (message)
            {
                case PlayerAttachedMsg _:
                    InterfaceController.PlayerAttached();
                    _playerAttached = true;
                    break;

                case PlayerDetachedMsg _:
                    InterfaceController.PlayerDetached();
                    _playerAttached = false;
                    break;
            }
        }

        public bool TryGetSlot(Slots slot, out IEntity item)
        {
            return _slots.TryGetValue(slot, out item);
        }
    }
}
