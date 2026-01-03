using System;
using System.Collections.Generic;
using System.Text;

namespace SCJMapper_V2.CryXMLlib
{
    /// <summary>
    /// Processes a CryXmlNodeRef and reports the node and its childs as XML string
    /// </summary>
    public class XmlTree
    {
        private readonly StringBuilder _sb = new StringBuilder();

        /// <summary>
        /// Return the derived XML text as string
        /// </summary>
        public string XML_string => _sb.ToString();

        /// <summary>
        /// Processes a CryXmlNodeRef to derive the XML formatted structure
        /// </summary>
        /// <param name="rootRef">The node to start from</param>
        public void BuildXML(CryXmlNodeRef rootRef)
        {
            _sb.Clear();
            
            if (rootRef == null)
            {
                return;
            }

            // Use stack-based iteration with a visited set to prevent infinite loops
            var visited = new HashSet<object>();
            var stack = new Stack<(CryXmlNodeRef node, int level, bool isClosingTag)>();
            stack.Push((rootRef, 0, false));

            int maxIterations = 100000; // Safety limit
            int iterations = 0;

            while (stack.Count > 0 && iterations < maxIterations)
            {
                iterations++;
                var (nodeRef, level, isClosingTag) = stack.Pop();
                
                if (nodeRef == null) continue;
                
                IXmlNode node = nodeRef;
                if (node == null) continue;

                if (isClosingTag)
                {
                    // Closing tag for a node with children
                    var closeTabs = new string('\t', level);
                    _sb.Append(closeTabs).Append("</").Append(node.getTag()).AppendLine(">");
                    continue;
                }

                // Check for cycles using reference equality
                if (visited.Contains(nodeRef))
                {
                    continue; // Skip already visited nodes
                }
                visited.Add(nodeRef);

                var tabs = new string('\t', level);

                // Build opening tag with attributes
                string tag = node.getTag() ?? "unknown";
                _sb.Append(tabs).Append('<').Append(tag);
                
                int attrCount = node.getNumAttributes();
                for (int ac = 0; ac < attrCount; ac++)
                {
                    node.getAttributeByIndex(ac, out string key, out string value);
                    _sb.Append(' ').Append(key ?? "").Append("=\"").Append(value ?? "").Append('"');
                }

                int childCount = node.getChildCount();
                if (childCount < 1)
                {
                    // Self-closing tag
                    _sb.AppendLine(" />");
                }
                else
                {
                    // Opening tag with children
                    _sb.AppendLine(">");
                    
                    // Push closing tag first (will be processed after children)
                    stack.Push((nodeRef, level, true));
                    
                    // Push children in reverse order so they're processed in correct order
                    for (int cc = childCount - 1; cc >= 0; cc--)
                    {
                        var childRef = node.getChild(cc);
                        if (childRef != null)
                        {
                            stack.Push((childRef, level + 1, false));
                        }
                    }
                }
            }
        }
    }
}
