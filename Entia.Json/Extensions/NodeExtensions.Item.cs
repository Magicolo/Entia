using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Json
{
    public static partial class NodeExtensions
    {
        public static Node[] Items(this Node node) => node.TryItems(out var items) ? items : Array.Empty<Node>();
        public static bool TryItems(this Node node, out Node[] items)
        {
            items = node.Children;
            return node.IsArray();
        }

        public static bool HasItem(this Node node, int index) => node.TryItem(index, out _);
        public static bool TryItem(this Node node, int index, out Node item)
        {
            if (node.TryItems(out var items)) return items.TryAt(index, out item);
            item = default;
            return false;
        }

        public static Node AddItem(this Node node, Node value) => node.IsArray() ? node.Add(value) : node;
        public static Node AddItem(this Node node, int index, Node value) => node.IsArray() ? node.AddAt(index, value) : node;
        public static Node ReplaceItem(this Node node, int index, Node value) =>
            node.HasItem(index) ? node.ReplaceAt(index, value) : node;
        public static Node RemoveItem(this Node node, int index) =>
            node.HasItem(index) ? node.RemoveAt(index) : node;

        public static Node MapItem(this Node node, int index, Func<Node, Node> map) =>
            node.TryItem(index, out var item) ? node.ReplaceAt(index, map(item)) : node;
        public static Node MapItems(this Node node, Func<Node, Node> map) =>
            node.With(node.Items().Select(map));
        public static Node MapItems(this Node node, Func<Node, int, Node> map) =>
            node.With(node.Items().Select(map));

        public static Node FilterItem(this Node node, int index, Func<Node, bool> filter) =>
            node.TryItem(index, out var item) && !filter(item) ? node.RemoveAt(index) : node;
        public static Node FilterItems(this Node node, Func<Node, bool> filter) =>
            node.With(node.Items().Where(filter).ToArray());
        public static Node FilterItems(this Node node, Func<Node, int, bool> filter) =>
            node.With(node.Items().Where(filter).ToArray());
    }
}