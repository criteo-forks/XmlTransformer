using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace XmlTransformer
{
    public static class XElementExtensions
    {
        public static string GetOptionalAttributeValue(this XElement element, string localName, string namespaceName = null)
        {
            XAttribute attr;
            if (String.IsNullOrEmpty(namespaceName))
            {
                attr = element.Attribute(localName);
            }
            else
            {
                attr = element.Attribute(XName.Get(localName, namespaceName));
            }
            return attr != null ? attr.Value : null;
        }

        public static string GetOptionalElementValue(this XContainer element, string localName, string namespaceName = null)
        {
            XElement child;
            if (String.IsNullOrEmpty(namespaceName))
            {
                child = element.ElementsNoNamespace(localName).FirstOrDefault();
            }
            else
            {
                child = element.Element(XName.Get(localName, namespaceName));
            }
            return child != null ? child.Value : null;
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this XContainer container, string localName)
        {
            return container.Elements().Where(e => e.Name.LocalName == localName);
        }

        public static IEnumerable<XElement> ElementsNoNamespace(this IEnumerable<XContainer> source, string localName)
        {
            return source.Elements().Where(e => e.Name.LocalName == localName);
        }

        // REVIEW: We can use a stack if the perf is bad for Except and MergeWith
        public static XElement Except(this XElement source, XElement target)
        {
            if (target == null)
            {
                return source;
            }

            var attributesToRemove = from e in source.Attributes()
                                     where AttributeEquals(e, target.Attribute(e.Name))
                                     select e;
            // Remove the attributes
            foreach (var a in attributesToRemove.ToList())
            {
                a.Remove();
            }

            foreach (var sourceChild in source.Elements().ToList())
            {
                var targetChild = FindElement(target, sourceChild);
                if (targetChild != null && !HasConflict(sourceChild, targetChild))
                {
                    Except(sourceChild, targetChild);
                    bool hasContent = sourceChild.HasAttributes || sourceChild.HasElements;
                    if (!hasContent)
                    {
                        // Remove the element if there is no content
                        sourceChild.Remove();
                        targetChild.Remove();
                    }
                }
            }
            return source;
        }

        public static XElement MergeWith(this XElement source, XElement target)
        {
            return MergeWith(source, target, null);
        }

        public static XElement MergeWith(this XElement source, XElement target, IDictionary<XName, Action<XElement, XElement>> nodeActions)
        {
            if (target == null)
            {
                return source;
            }

            // Merge the attributes
            foreach (var targetAttribute in target.Attributes())
            {
                var sourceAttribute = source.Attribute(targetAttribute.Name);
                if (sourceAttribute == null)
                {
                    source.Add(targetAttribute);
                }
            }

            // Go through the elements to be merged
            foreach (var targetChild in target.Elements())
            {
                var sourceChild = FindElement(source, targetChild);
                if (sourceChild != null && !HasConflict(sourceChild, targetChild))
                {
                    // Other wise merge recursively
                    sourceChild.MergeWith(targetChild, nodeActions);
                }
                else
                {
                    Action<XElement, XElement> nodeAction;
                    if (nodeActions != null && nodeActions.TryGetValue(targetChild.Name, out nodeAction))
                    {
                        nodeAction(source, targetChild);
                    }
                    else
                    {
                        // If that element is null then add that node
                        source.Add(targetChild);
                    }
                }
            }
            return source;
        }

        private static XElement FindElement(XElement source, XElement targetChild)
        {
            // Get all of the elements in the source that match this name
            var sourceElements = source.Elements(targetChild.Name).ToList();

            // Try to find the best matching element based on attribute names and values
            sourceElements.Sort((a, b) => Compare(targetChild, a, b));

            return sourceElements.FirstOrDefault();
        }

        private static int Compare(XElement target, XElement left, XElement right)
        {
            Debug.Assert(left.Name == right.Name);

            // First check how much attribute names and values match
            int leftExactMathes = CountMatches(left, target, AttributeEquals);
            int rightExactMathes = CountMatches(right, target, AttributeEquals);

            if (leftExactMathes == rightExactMathes)
            {
                // Then check which names match
                int leftNameMatches = CountMatches(left, target, (a, b) => a.Name == b.Name);
                int rightNameMatches = CountMatches(right, target, (a, b) => a.Name == b.Name);

                return rightNameMatches.CompareTo(leftNameMatches);
            }

            return rightExactMathes.CompareTo(leftExactMathes);
        }

        private static int CountMatches(XElement left, XElement right, Func<XAttribute, XAttribute, bool> matcher)
        {
            return (from la in left.Attributes()
                    from ta in right.Attributes()
                    where matcher(la, ta)
                    select la).Count();
        }

        private static bool HasConflict(XElement source, XElement target)
        {
            // Get all attributes as name value pairs
            var sourceAttr = source.Attributes().ToDictionary(a => a.Name, a => a.Value);
            // Loop over all the other attributes and see if there are
            foreach (var targetAttr in target.Attributes())
            {
                string sourceValue;
                // if any of the attributes are in the source (names match) but the value doesn't match then we've found a conflict
                if (sourceAttr.TryGetValue(targetAttr.Name, out sourceValue) && sourceValue != targetAttr.Value)
                {
                    return true;
                }
            }
            return false;
        }

        public static void RemoveAttributes(this XElement element, Func<XAttribute, bool> condition)
        {
            element.Attributes()
                   .Where(condition)
                   .ToList()
                   .Remove();

            element.Descendants()
                   .ToList()
                   .ForEach(e => RemoveAttributes(e, condition));
        }

        private static bool AttributeEquals(XAttribute source, XAttribute target)
        {
            if (source == null && target == null)
            {
                return true;
            }

            if (source == null || target == null)
            {
                return false;
            }
            return source.Name == target.Name && source.Value == target.Value;
        }

        public static XElement ToXElement(this XmlElement xml)
        {
            XmlDocument doc = new XmlDocument();

            doc.AppendChild(doc.ImportNode(xml, true));

            return XElement.Parse(doc.InnerXml);
        }

        public static XmlElement ToXmlElement(this XElement xml)
        {
            var doc = new XmlDocument();
            return doc.ReadNode(xml.CreateReader()) as XmlElement;
        }
    }
}