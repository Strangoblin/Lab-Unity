#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 工具窗口的资产拖拽落点 —— 从 Project 窗口把资产拖进窗口，把资产路径交给调用方。
/// </summary>
///
/// <remarks>
/// 必须在 <c>OnGUI</c> 的最前面、任何控件之前调用：
/// 文本域自己也会处理对象拖放，滚动视图还会改写 <c>Event.current.mousePosition</c>，
/// 先处理就能保证事件没被它们吃掉、坐标也还是窗口坐标系。
/// 只接受已落盘的资产（AssetDatabase 内有路径）；场景对象、瞬态对象与类型不符者一律拒绝。
/// </remarks>
public static class AssetPathDropTarget
{
    /// <summary>在窗口内任意位置接受拖放。拖入合法资产并松手时返回 true。</summary>
    /// <param name="owner">接收拖放的窗口。</param>
    /// <param name="accept">资产过滤器；返回 false 的资产视为拒绝。</param>
    /// <param name="asset">松手时返回被拖入的资产；未接受时为 null。</param>
    public static bool TryConsumeWindowDrop(EditorWindow owner, System.Predicate<UnityEngine.Object> accept,
        out UnityEngine.Object asset)
    {
        asset = null;
        if (owner == null)
        {
            return false;
        }

        Vector2 size = owner.position.size;
        var rect = new Rect(0f, 0f, Mathf.Max(1f, size.x), Mathf.Max(1f, size.y));
        return TryConsume(rect, accept, out asset);
    }

    /// <summary>在 <paramref name="rect"/>（窗口坐标系）上处理拖拽事件。</summary>
    public static bool TryConsume(Rect rect, System.Predicate<UnityEngine.Object> accept, out UnityEngine.Object asset)
    {
        asset = null;

        Event current = Event.current;
        if (current == null
            || (current.type != EventType.DragUpdated && current.type != EventType.DragPerform)
            || !rect.Contains(current.mousePosition))
        {
            return false;
        }

        bool acceptable = TryFindAsset(accept, out UnityEngine.Object dragged);
        DragAndDrop.visualMode = acceptable ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
        current.Use();

        if (!acceptable || current.type != EventType.DragPerform)
        {
            return false;
        }

        asset = dragged;
        return true;
    }

    private static bool TryFindAsset(System.Predicate<UnityEngine.Object> accept, out UnityEngine.Object asset)
    {
        asset = null;

        UnityEngine.Object[] references = DragAndDrop.objectReferences;
        if (references == null)
        {
            return false;
        }

        foreach (UnityEngine.Object candidate in references)
        {
            if (candidate == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(candidate)))
            {
                continue;
            }

            if (accept != null && !accept(candidate))
            {
                continue;
            }

            asset = candidate;
            return true;
        }

        return false;
    }
}
#endif
