using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Exerussus.Outline.UI.Tests
{
    public sealed class OutlineUiTests
    {
        private readonly List<Object> _objects = new();
        private OutlineUiStyle _style;

        [SetUp]
        public void SetUp()
        {
            _style = ScriptableObject.CreateInstance<OutlineUiStyle>();
            _objects.Add(_style);
        }

        [TearDown]
        public void TearDown()
        {
            OutlineUi.HideAll();
            foreach (var o in _objects)
                if (o != null)
                    Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private static int FilterCount(VisualElement e)
        {
            var f = e.style.filter;
            return f.keyword == StyleKeyword.Undefined && f.value != null ? f.value.Count : 0;
        }

        [Test]
        public void Show_AddsFilter_Hide_RemovesIt()
        {
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            Assert.IsTrue(h.IsAlive);
            Assert.AreEqual(1, FilterCount(e));
            h.Hide();
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(0, FilterCount(e));
        }

        [Test]
        public void UserFilters_ArePreserved()
        {
            var e = new VisualElement();
            var blur = new FilterFunction(FilterFunctionType.Blur);
            blur.AddParameter(new FilterParameter(2f));
            e.style.filter = new List<FilterFunction> { blur };

            var h1 = OutlineUi.Show(e, _style);
            var h2 = OutlineUi.Show(e, _style);
            Assert.AreEqual(3, FilterCount(e));
            Assert.AreEqual(FilterFunctionType.Blur, e.style.filter.value[0].type);
            h1.Hide();
            Assert.AreEqual(2, FilterCount(e));
            h2.Hide();
            Assert.AreEqual(1, FilterCount(e));
            Assert.AreEqual(FilterFunctionType.Blur, e.style.filter.value[0].type);
        }

        [Test]
        public void SetStyle_WithDuration_KeepsHandle()
        {
            var other = ScriptableObject.CreateInstance<OutlineUiStyle>();
            _objects.Add(other);
            other.outerWidth = 60f;
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            h.SetStyle(other, 0.5f);
            Assert.IsTrue(h.IsAlive);
            Assert.AreSame(other, OutlineUi.GetStyle(h.Slot));
            Assert.AreEqual(1, FilterCount(e));
        }

        [Test]
        public void Suspend_RemovesFilter_Resume_RestoresIt()
        {
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            h.SetSuspended(true);
            Assert.IsTrue(h.IsAlive);
            Assert.IsTrue(h.IsSuspended);
            Assert.AreEqual(0, FilterCount(e));
            h.SetSuspended(false);
            Assert.IsFalse(h.IsSuspended);
            Assert.AreEqual(1, FilterCount(e));
        }

        [Test]
        public void SuspendWithin_TouchesOnlyDescendants()
        {
            var root = new VisualElement();
            var child = new VisualElement();
            var outside = new VisualElement();
            root.Add(child);
            var hChild = OutlineUi.Show(child, _style);
            var hOutside = OutlineUi.Show(outside, _style);
            OutlineUi.SetSuspendedWithin(root, true);
            Assert.IsTrue(hChild.IsSuspended);
            Assert.IsFalse(hOutside.IsSuspended);
            Assert.AreEqual(0, FilterCount(child));
            Assert.AreEqual(1, FilterCount(outside));
        }

        [Test]
        public void Freeze_KeepsFilter_AndStopsSlotTime()
        {
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            h.SetFrozen(true);
            Assert.IsTrue(h.IsFrozen);
            Assert.AreEqual(1, FilterCount(e));
            float t0 = OutlineUi.SlotNow(h.Slot);
            System.Threading.Thread.Sleep(20);
            Assert.AreEqual(t0, OutlineUi.SlotNow(h.Slot));
            h.SetFrozen(false);
            Assert.IsFalse(h.IsFrozen);
        }

        [Test]
        public void FadeOutAndHide_WhenFrozen_HidesAtOnce()
        {
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            h.SetFrozen(true);
            h.FadeOutAndHide(1f);
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(0, FilterCount(e));
        }

        [Test]
        public void Overflow_EvictsLowerPriority_ElseInvalid()
        {
            var e = new VisualElement();
            for (int i = 0; i < OutlineUi.MaxEntries; i++)
                OutlineUi.Show(e, _style, OutlineUiOptions.Priority(0));
            Assert.AreEqual(OutlineUi.MaxEntries, OutlineUi.AliveCount);

            var important = OutlineUi.Show(new VisualElement(), _style, OutlineUiOptions.Priority(5));
            Assert.IsTrue(important.IsAlive);
            Assert.AreEqual(OutlineUi.MaxEntries, OutlineUi.AliveCount);

            LogAssert.ignoreFailingMessages = true;
            var same = OutlineUi.Show(new VisualElement(), _style, OutlineUiOptions.Priority(0));
            LogAssert.ignoreFailingMessages = false;
            Assert.IsFalse(same.IsAlive);
        }

        [Test]
        public void Flash_WithStyle_UsesGivenStyle()
        {
            var e = new VisualElement();
            var h = OutlineUiFx.Flash(e, _style, 0.3f);
            Assert.IsTrue(h.IsAlive);
            Assert.AreSame(_style, OutlineUi.GetStyle(h.Slot));
        }

        [Test]
        public void Target_PicksStyleByState()
        {
            var hover = ScriptableObject.CreateInstance<OutlineUiStyle>();
            var selected = ScriptableObject.CreateInstance<OutlineUiStyle>();
            _objects.Add(hover);
            _objects.Add(selected);
            var t = new OutlineUiTarget { outlineStyle = _style, hoverStyle = hover, selectedStyle = selected };
            Assert.AreSame(_style, t.CurrentStyle);
            t.selected = true;
            Assert.AreSame(selected, t.CurrentStyle);
            t.highlighted = false;
            t.selected = false;
            Assert.IsNull(t.CurrentStyle);
        }

        [Test]
        public void StaleHandle_IsNoOp()
        {
            var e = new VisualElement();
            var h = OutlineUi.Show(e, _style);
            h.Hide();
            Assert.DoesNotThrow(() =>
            {
                h.SetStyle(_style, 1f);
                h.FadeTo(0f, 1f);
                h.Hide();
            });
        }
    }
}
