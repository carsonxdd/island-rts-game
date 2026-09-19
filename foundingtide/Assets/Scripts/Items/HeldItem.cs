using System;
using UnityEngine;

/// <summary>
/// Shows the tool the player's character is holding (2026-09-02): the art
/// prefab for the current <see cref="ItemDef"/> instantiated under the
/// <see cref="socket"/> (a "HandSocket" child on the Model, placed by the
/// Opening Sequence setup tool). The id→prefab table is wired by the same tool
/// from <c>Assets/Art/Prefabs/Tools/</c>; an item with no art simply shows
/// nothing rather than erroring.
///
/// Visual only. What the character can do is decided by <see cref="Unlocks"/>,
/// never by what is in the hand.
/// </summary>
public class HeldItem : MonoBehaviour
{
    [Serializable]
    public struct ItemArt
    {
        public string itemId;
        public GameObject prefab;
    }

    [Tooltip("Where the tool is mounted — a child transform on the character's Model.")]
    public Transform socket;

    [Tooltip("Item id → art prefab (wired by the Opening Sequence setup tool).")]
    public ItemArt[] art;

    private ItemDef current;
    private GameObject instance;

    public ItemDef Current => current;

    public void Equip(ItemDef item)
    {
        if (item == current) return;
        current = item;

        if (instance != null)
        {
            Destroy(instance);
            instance = null;
        }
        if (item == null) return;

        GameObject prefab = PrefabFor(item.id);
        if (prefab == null) return;

        Transform parent = socket != null ? socket : transform;
        instance = Instantiate(prefab, parent);
        instance.name = "Held_" + item.id;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        // Art prefabs carry no colliders, but never let a held prop become a raycast target
        Collider[] cols = instance.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
    }

    GameObject PrefabFor(string id)
    {
        if (art == null) return null;
        for (int i = 0; i < art.Length; i++)
            if (art[i].itemId == id) return art[i].prefab;
        return null;
    }
}
