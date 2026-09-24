using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
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
