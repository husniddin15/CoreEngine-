using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoreEngine.Spike.UI
{
    /// <summary>A panel that can live in any dock area: a localised title and its content.</summary>
    public sealed class DockPanel
    {
        public DockPanel(string titleKey, VisualElement content)
        {
            TitleKey = titleKey;
            Content = content;
            content.AddToClassList("dock-panel");
        }

        public string TitleKey { get; }
        public VisualElement Content { get; }
        public DockArea? Area { get; internal set; }
        internal Label? Tab { get; set; }
    }

    /// <summary>
    /// Phase 0.6 spike of dockable panels (docs/03 §12.1): a tab strip over a content area. Drag a tab
    /// onto another dock area to move the panel there; click a tab to show it. Areas are resized by the
    /// TwoPaneSplitViews around them. Layout saving per mode comes in Phase 1.
    /// </summary>
    public sealed class DockArea : VisualElement
    {
        const float DragThreshold = 6f;
        static readonly List<DockArea> Areas = new List<DockArea>();

        readonly VisualElement tabStrip;
        readonly VisualElement body;
        readonly Label emptyHint;
        readonly List<DockPanel> panels = new List<DockPanel>();
        readonly Func<string, string> localize;
        DockPanel? active;

        // Drag state (one drag at a time).
        static DockPanel? dragged;
        static Vector2 dragStart;
        static bool dragging;
        static Label? ghost;
        static DockArea? dropTarget;

        public DockArea(string name, Func<string, string> localize)
        {
            this.name = name;
            this.localize = localize;
            AddToClassList("dock-area");
            tabStrip = new VisualElement();
            tabStrip.AddToClassList("dock-tabs");
            body = new VisualElement();
            body.AddToClassList("dock-body");
            emptyHint = new Label();
            emptyHint.AddToClassList("dock-empty");
            Add(tabStrip);
            Add(body);
            body.Add(emptyHint);
            Areas.Add(this);
            RegisterCallback<DetachFromPanelEvent>(_ => Areas.Remove(this));
            RefreshTitles();
        }

        public IReadOnlyList<DockPanel> Panels => panels;

        public void AddPanel(DockPanel panel)
        {
            panel.Area?.RemovePanel(panel);
            panel.Area = this;
            panels.Add(panel);
            var tab = new Label(localize(panel.TitleKey));
            tab.AddToClassList("dock-tab");
            panel.Tab = tab;
            tab.RegisterCallback<PointerDownEvent>(e => OnTabPointerDown(e, panel));
            tab.RegisterCallback<PointerMoveEvent>(e => OnTabPointerMove(e, panel));
            tab.RegisterCallback<PointerUpEvent>(e => OnTabPointerUp(e, panel));
            tabStrip.Add(tab);
            body.Add(panel.Content);
            Select(panel);
        }

        public void RemovePanel(DockPanel panel)
        {
            if (!panels.Remove(panel)) return;
            panel.Tab?.RemoveFromHierarchy();
            panel.Content.RemoveFromHierarchy();
            panel.Area = null;
            if (active == panel) active = null;
            if (active == null && panels.Count > 0) Select(panels[0]);
            UpdateEmptyHint();
        }

        public void Select(DockPanel panel)
        {
            active = panel;
            foreach (var p in panels)
            {
                bool on = p == panel;
                p.Content.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                p.Tab?.EnableInClassList("dock-tab--active", on);
            }
            UpdateEmptyHint();
        }

        public void RefreshTitles()
        {
            foreach (var p in panels) if (p.Tab != null) p.Tab.text = localize(p.TitleKey);
            emptyHint.text = localize("dock.empty");
        }

        void UpdateEmptyHint() => emptyHint.style.display = panels.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;

        void OnTabPointerDown(PointerDownEvent e, DockPanel panel)
        {
            if (e.button != 0) return;
            dragged = panel;
            dragStart = e.position;
            dragging = false;
            panel.Tab!.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnTabPointerMove(PointerMoveEvent e, DockPanel panel)
        {
            var tab = panel.Tab!;
            if (dragged != panel || !tab.HasPointerCapture(e.pointerId)) return;
            if (!dragging && Vector2.Distance(dragStart, e.position) < DragThreshold) return;
            if (!dragging)
            {
                dragging = true;
                ghost = new Label(localize(panel.TitleKey)) { pickingMode = PickingMode.Ignore };
                ghost.AddToClassList("dock-ghost");
                tab.panel?.visualTree.Add(ghost);
            }
            ghost!.style.left = e.position.x + 12;
            ghost.style.top = e.position.y + 8;
            SetDropTarget(AreaAt(e.position));
        }

        void OnTabPointerUp(PointerUpEvent e, DockPanel panel)
        {
            if (dragged != panel) return;
            panel.Tab!.ReleasePointer(e.pointerId);
            if (dragging)
            {
                var target = AreaAt(e.position);
                if (target != null && target != panel.Area) target.AddPanel(panel);
            }
            else
            {
                panel.Area?.Select(panel);
            }
            ghost?.RemoveFromHierarchy();
            ghost = null;
            SetDropTarget(null);
            dragged = null;
            dragging = false;
        }

        static DockArea? AreaAt(Vector2 position)
        {
            foreach (var area in Areas)
                if (area.resolvedStyle.display != DisplayStyle.None && area.worldBound.Contains(position)) return area;
            return null;
        }

        static void SetDropTarget(DockArea? area)
        {
            if (dropTarget == area) return;
            dropTarget?.RemoveFromClassList("dock-area--drop-target");
            dropTarget = area;
            dropTarget?.AddToClassList("dock-area--drop-target");
        }
    }
}
