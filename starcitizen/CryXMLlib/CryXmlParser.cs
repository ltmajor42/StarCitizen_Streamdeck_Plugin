using System;
using System.Text;
using starcitizen.Core;

namespace SCJMapper_V2.CryXMLlib
{
    /// <summary>
    /// Simple, efficient CryXML binary parser for .NET 8.
    /// Directly converts binary CryXML to XML string without complex object graphs.
    /// </summary>
    public static class CryXmlParser
    {
        private const string CryXmlSignature = "CryXmlB";

        /// <summary>
        /// Parse binary CryXML data and return XML string.
        /// Returns null if parsing fails.
        /// </summary>
        public static string Parse(byte[] data)
        {
            if (data == null || data.Length < 44)
            {
                PluginLog.Error($"CryXmlParser: Data too small ({data?.Length ?? 0} bytes)");
                return null;
            }

            try
            {
                // Verify signature
                var sig = Encoding.ASCII.GetString(data, 0, 7);
                if (sig != CryXmlSignature)
                {
                    PluginLog.Error($"CryXmlParser: Invalid signature '{sig}'");
                    return null;
                }

                // Read header (44 bytes)
                int pos = 8; // Skip signature + null terminator
                uint xmlSize = ReadUInt32(data, ref pos);
                uint nodeTablePos = ReadUInt32(data, ref pos);
                uint nodeCount = ReadUInt32(data, ref pos);
                uint attrTablePos = ReadUInt32(data, ref pos);
                uint attrCount = ReadUInt32(data, ref pos);
                uint childTablePos = ReadUInt32(data, ref pos);
                uint childCount = ReadUInt32(data, ref pos);
                uint stringTablePos = ReadUInt32(data, ref pos);
                uint stringTableSize = ReadUInt32(data, ref pos);

                PluginLog.Debug($"CryXmlParser header: nodes={nodeCount} @ {nodeTablePos}, attrs={attrCount} @ {attrTablePos}, children={childCount} @ {childTablePos}, strings={stringTableSize} @ {stringTablePos}");

                // Validate positions
                if (nodeTablePos >= data.Length || attrTablePos >= data.Length || 
                    childTablePos >= data.Length || stringTablePos >= data.Length)
                {
                    PluginLog.Error($"CryXmlParser: Table positions exceed data length ({data.Length})");
                    return null;
                }

                // Read all tables
                var nodes = ReadNodes(data, nodeTablePos, nodeCount);
                PluginLog.Debug($"CryXmlParser: Read {nodes.Length} nodes");
                
                var attrs = ReadAttributes(data, attrTablePos, attrCount);
                PluginLog.Debug($"CryXmlParser: Read {attrs.Length} attributes");
                
                var children = ReadChildren(data, childTablePos, childCount);
                PluginLog.Debug($"CryXmlParser: Read {children.Length} children");

                int strLen = (int)Math.Min(stringTableSize, data.Length - stringTablePos);
                var strings = data.AsSpan((int)stringTablePos, strLen);
                PluginLog.Debug($"CryXmlParser: String table {strLen} bytes");

                // Build XML using stack to avoid deep recursion
                var sb = new StringBuilder(1024 * 1024); // Pre-allocate 1MB
                PluginLog.Debug("CryXmlParser: Building XML...");
                
                BuildXmlIterative(sb, nodes, attrs, children, strings);
                
                PluginLog.Debug($"CryXmlParser: Built XML {sb.Length} chars");
                return sb.ToString();
            }
            catch (Exception ex)
            {
                PluginLog.Error($"CryXmlParser exception: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }

        private static void BuildXmlIterative(StringBuilder sb, Node[] nodes, Attr[] attrs, uint[] children, ReadOnlySpan<byte> strings)
        {
            if (nodes.Length == 0) return;

            // Stack-based traversal: (nodeIndex, depth, isClosing)
            var stack = new System.Collections.Generic.Stack<(int nodeIdx, int depth, bool isClosing)>();
            stack.Push((0, 0, false));

            int iterations = 0;
            int maxIterations = nodes.Length * 3; // Safety limit

            while (stack.Count > 0 && iterations++ < maxIterations)
            {
                var (nodeIdx, depth, isClosing) = stack.Pop();
                
                if (nodeIdx < 0 || nodeIdx >= nodes.Length) continue;
                
                var node = nodes[nodeIdx];
                var indent = new string('\t', depth);
                var tag = ReadString(strings, node.TagOffset);

                if (isClosing)
                {
                    sb.Append(indent).Append("</").Append(tag).AppendLine(">");
                    continue;
                }

                // Opening tag
                sb.Append(indent).Append('<').Append(tag);

                // Add attributes
                for (int i = 0; i < node.AttrCount && (node.FirstAttrIndex + i) < attrs.Length; i++)
                {
                    var attr = attrs[node.FirstAttrIndex + i];
                    var key = ReadString(strings, attr.KeyOffset);
                    var value = ReadString(strings, attr.ValueOffset);
                    sb.Append(' ').Append(key).Append("=\"").Append(EscapeXml(value)).Append('"');
                }

                if (node.ChildCount == 0)
                {
                    sb.AppendLine(" />");
                }
                else
                {
                    sb.AppendLine(">");

                    // Push closing tag first
                    stack.Push((nodeIdx, depth, true));

                    // Push children in reverse order
                    for (int i = node.ChildCount - 1; i >= 0; i--)
                    {
                        int childIdx = (int)node.FirstChildIndex + i;
                        if (childIdx >= 0 && childIdx < children.Length)
                        {
                            stack.Push(((int)children[childIdx], depth + 1, false));
                        }
                    }
                }
            }

            if (iterations >= maxIterations)
            {
                PluginLog.Warn($"CryXmlParser: Hit iteration limit ({maxIterations})");
            }
        }

        private static string ReadString(ReadOnlySpan<byte> strings, uint offset)
        {
            if (offset >= strings.Length)
                return "";

            var slice = strings[(int)offset..];
            int len = slice.IndexOf((byte)0);
            if (len < 0) len = Math.Min(slice.Length, 1024); // Limit string length
            if (len == 0) return "";
            return Encoding.UTF8.GetString(slice[..len]);
        }

        private static string EscapeXml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;");
        }

        private static uint ReadUInt32(byte[] data, ref int pos)
        {
            uint val = BitConverter.ToUInt32(data, pos);
            pos += 4;
            return val;
        }

        private static Node[] ReadNodes(byte[] data, uint tablePos, uint count)
        {
            var nodes = new Node[count];
            int pos = (int)tablePos;
            for (int i = 0; i < count && pos + 28 <= data.Length; i++)
            {
                nodes[i] = new Node
                {
                    TagOffset = BitConverter.ToUInt32(data, pos),
                    ContentOffset = BitConverter.ToUInt32(data, pos + 4),
                    AttrCount = BitConverter.ToUInt16(data, pos + 8),
                    ChildCount = BitConverter.ToUInt16(data, pos + 10),
                    ParentIndex = BitConverter.ToUInt32(data, pos + 12),
                    FirstAttrIndex = BitConverter.ToUInt32(data, pos + 16),
                    FirstChildIndex = BitConverter.ToUInt32(data, pos + 20)
                };
                pos += 28; // Node struct size (including 4-byte padding)
            }
            return nodes;
        }

        private static Attr[] ReadAttributes(byte[] data, uint tablePos, uint count)
        {
            var attrs = new Attr[count];
            int pos = (int)tablePos;
            for (int i = 0; i < count && pos + 8 <= data.Length; i++)
            {
                attrs[i] = new Attr
                {
                    KeyOffset = BitConverter.ToUInt32(data, pos),
                    ValueOffset = BitConverter.ToUInt32(data, pos + 4)
                };
                pos += 8;
            }
            return attrs;
        }

        private static uint[] ReadChildren(byte[] data, uint tablePos, uint count)
        {
            var children = new uint[count];
            int pos = (int)tablePos;
            for (int i = 0; i < count && pos + 4 <= data.Length; i++)
            {
                children[i] = BitConverter.ToUInt32(data, pos);
                pos += 4;
            }
            return children;
        }

        private struct Node
        {
            public uint TagOffset;
            public uint ContentOffset;
            public ushort AttrCount;
            public ushort ChildCount;
            public uint ParentIndex;
            public uint FirstAttrIndex;
            public uint FirstChildIndex;
        }

        private struct Attr
        {
            public uint KeyOffset;
            public uint ValueOffset;
        }
    }
}
