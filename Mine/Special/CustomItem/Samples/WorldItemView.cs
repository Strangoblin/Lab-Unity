using CelToon.CustomItem.Adapters;
using UnityEngine;

namespace CelToon.CustomItem.Samples
{
    // A scene presentation binds an existing instance. It never grants inventory in Awake.
    public sealed class WorldItemView : MonoBehaviour
    {
        public string SpawnId;
        public Renderer[] Visuals;
        public Collider[] Colliders;
        public string ItemId { get; private set; }
        public bool Available { get; private set; }
        private ItemRangeWorkbench owner;

        public void Bind(ItemRangeWorkbench workbench, string itemId)
        { owner = workbench; ItemId = itemId; Refresh(); }

        public void Refresh()
        {
            var location = string.IsNullOrEmpty(ItemId) ? null : owner?.Store?.GetLocation(ItemId);
            Available = location != null && location.ContainerId == ItemEquipmentService.WorldId;
            if (Visuals != null) foreach (var visual in Visuals) if (visual != null) visual.enabled = Available;
            if (Colliders != null) foreach (var collider in Colliders) if (collider != null) collider.enabled = Available;
        }

        public bool TryPickup()
        {
            if (owner == null || !Available) return false;
            // Visibility follows the committed location, so failure leaves the physical item intact.
            bool pickedUp = owner.Pickup(ItemId);
            Refresh(); return pickedUp;
        }
    }
}
