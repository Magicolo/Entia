using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Entia.Core;

namespace Entia.Json
{
    public static partial class NodeExtensions
    {
        public readonly struct MemberEnumerable : IEnumerable<MemberEnumerator, (string key, Node value)>
        {
            readonly Node _node;
            public MemberEnumerable(Node node) { _node = node; }
            public MemberEnumerator GetEnumerator() => new MemberEnumerator(_node);
            IEnumerator<(string key, Node value)> IEnumerable<(string key, Node value)>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public struct MemberEnumerator : IEnumerator<(string key, Node value)>
        {
            public (string key, Node value) Current { get; private set; }
            object IEnumerator.Current => Current;

            readonly Node[] _nodes;
            int _index;

            public MemberEnumerator(Node node)
            {
                Current = default;
                _nodes = node.Children;
                _index = 0;
            }

            public bool MoveNext()
            {
                if (_index < _nodes.Length && _nodes[_index].TryString(out var key))
                {
                    Current = (key, _nodes[_index + 1]);
                    _index += 2;
                    return true;
                }
                return false;
            }

            public void Reset() => _index = 0;
            public void Dispose() => this = default;
        }

        public static MemberEnumerable Members(this Node node) => node.TryMembers(out var members) ? members : new MemberEnumerable(Node.Null);
        public static bool TryMembers(this Node node, out MemberEnumerable members)
        {
            members = new MemberEnumerable(node);
            return node.IsObject();
        }

        public static bool HasMember(this Node node, string key) => node.TryMember(key, out _, out _);
        public static bool TryMember(this Node node, string key, out Node value) => node.TryMember(key, out value, out _);
        public static bool TryMember(this Node node, string key, out Node value, out int index)
        {
            if (node.IsObject() && node.Children.Length > 0)
            {
                for (index = 0; index < node.Children.Length; index += 2)
                {
                    if (node.Children[index].AsString() == key)
                    {
                        value = node.Children[index + 1];
                        return true;
                    }
                }
            }

            value = default;
            index = default;
            return false;
        }

        public static Node AddMember(this Node node, string key, Node value) =>
            node.TryMember(key, out _, out var index) ? node.ReplaceAt(index, key, value) :
            node.IsObject() ? node.Add(key, value) : node;
        public static Node RemoveMember(this Node node, string key) =>
            node.TryMember(key, out _, out var index) ? node.RemoveAt(index, 2) : node;

        public static Node MapMember(this Node node, string key, Func<Node, Node> map) =>
            node.MapMember(key, value => (key, map(value)));
        public static Node MapMember(this Node node, string key, Func<Node, (string key, Node value)> map)
        {
            if (node.TryMember(key, out var value, out var index))
            {
                (key, value) = map(value);
                return node.ReplaceAt(index, key, value);
            }
            else return node;
        }

        public static Node MapMembers(this Node node, Func<string, Node, Node> map) =>
            node.MapMembers((key, value) => (key, map(key, value)));
        public static Node MapMembers(this Node node, Func<string, Node, (string key, Node value)> map)
        {
            if (node.IsObject() && node.Children.Length > 0)
            {
                var children = new Node[node.Children.Length];
                for (int i = 0; i < children.Length; i += 2)
                    (children[i], children[i + 1]) = map(node.Children[i].AsString(), node.Children[i + 1]);
                return node.With(children);
            }
            else return node;
        }

        public static Node FilterMember(this Node node, string key, Func<Node, bool> filter) =>
            node.TryMember(key, out var value, out var index) && !filter(value) ? node.RemoveAt(index, 2) : node;
        public static Node FilterMembers(this Node node, Func<Node, bool> filter) =>
            node.FilterMembers((_, value) => filter(value));
        public static Node FilterMembers(this Node node, Func<string, Node, bool> filter) =>
            node.With(node.Members()
                .Where(pair => filter(pair.key, pair.value))
                .SelectMany(pair => new[] { Node.String(pair.key), pair.value })
                .ToArray());
    }
}