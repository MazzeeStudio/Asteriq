using System.Text;
using System.Xml;

namespace Asteriq.Services;

/// <summary>
/// Deserializes CryEngine binary XML (CryXmlB) format to standard XML.
/// Based on the format used by Star Citizen's Data.p4k archives.
/// </summary>
public static class CryXmlService
{
    private const string CryXmlBHeader = "CryXmlB";
    private const string CryXmlHeader = "CryXml";
    private const string Cry3SdkHeader = "CRY3SDK";

    /// <summary>
    /// Checks if the data is in CryXML binary format
    /// </summary>
    public static bool IsCryXmlB(byte[] data)
    {
        if (data == null || data.Length < 7)
            return false;

        // Check for known headers
        var header = Encoding.ASCII.GetString(data, 0, Math.Min(7, data.Length));
        return header.StartsWith(CryXmlBHeader) ||
               header.StartsWith(CryXmlHeader) ||
               header.StartsWith(Cry3SdkHeader);
    }

    /// <summary>
    /// Checks if the data starts with regular XML
    /// </summary>
    public static bool IsRegularXml(byte[] data)
    {
        if (data == null || data.Length < 1)
            return false;

        // Regular XML starts with '<'
        return data[0] == (byte)'<';
    }

    /// <summary>
    /// Deserializes CryXmlB binary data to an XmlDocument
    /// </summary>
    public static XmlDocument? Deserialize(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        // Check if it's already regular XML
        if (IsRegularXml(data))
        {
            try
            {
                var doc = new XmlDocument();
                var xmlContent = Encoding.UTF8.GetString(data);
                doc.LoadXml(xmlContent);
                System.Diagnostics.Debug.WriteLine("[CryXmlService] Parsed as regular XML");
                return doc;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CryXmlService] Failed to parse as regular XML: {ex.Message}");
                return null;
            }
        }

        // Check if it's CryXmlB format
        if (!IsCryXmlB(data))
        {
            System.Diagnostics.Debug.WriteLine("[CryXmlService] Data is not in CryXmlB or regular XML format");
            return null;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine("[CryXmlService] Parsing as CryXmlB binary format");
            return ParseCryXmlB(data);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CryXmlService] Failed to parse CryXmlB: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Deserializes CryXmlB binary data to an XML string
    /// </summary>
    public static string? DeserializeToString(byte[] data)
    {
        var doc = Deserialize(data);
        if (doc == null)
            return null;

        using var stringWriter = new StringWriter();
        using var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = false
        });

        doc.WriteTo(xmlWriter);
        xmlWriter.Flush();
        return stringWriter.ToString();
    }

    private static XmlDocument ParseCryXmlB(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        // Header structure (44 bytes total):
        // - szSignature: 8 bytes ("CryXmlB\0")
        // - nXMLSize: 4 bytes (total XML data size)
        // - nNodeTablePosition: 4 bytes
        // - nNodeCount: 4 bytes
        // - nAttributeTablePosition: 4 bytes
        // - nAttributeCount: 4 bytes
        // - nChildTablePosition: 4 bytes
        // - nChildCount: 4 bytes
        // - nStringDataPosition: 4 bytes
        // - nStringDataSize: 4 bytes

        var headerBytes = reader.ReadBytes(8);
        var header = Encoding.ASCII.GetString(headerBytes).TrimEnd('\0');

        if (header != "CryXmlB" && header != "CryXml" && !header.StartsWith("CRY"))
        {
            throw new InvalidDataException($"Invalid CryXmlB header: {header}");
        }

        var xmlSize = reader.ReadUInt32();
        var nodeTableOffset = reader.ReadUInt32();
        var nodeTableCount = reader.ReadUInt32();
        var attributeTableOffset = reader.ReadUInt32();
        var attributeTableCount = reader.ReadUInt32();
        var childTableOffset = reader.ReadUInt32();
        var childTableCount = reader.ReadUInt32();
        var stringTableOffset = reader.ReadUInt32();
        var stringTableSize = reader.ReadUInt32();

        System.Diagnostics.Debug.WriteLine($"[CryXmlService] CryXmlB: xmlSize={xmlSize}, nodes={nodeTableCount}@{nodeTableOffset}, attrs={attributeTableCount}@{attributeTableOffset}, children={childTableCount}@{childTableOffset}, strings={stringTableSize}bytes@{stringTableOffset}");

        // Read string data table
        ms.Position = stringTableOffset;
        var stringData = reader.ReadBytes((int)stringTableSize);

        // Build string lookup from offsets
        var strings = new Dictionary<int, string>();
        int currentOffset = 0;
        while (currentOffset < stringData.Length)
        {
            var startOffset = currentOffset;
            while (currentOffset < stringData.Length && stringData[currentOffset] != 0)
            {
                currentOffset++;
            }

            var str = Encoding.UTF8.GetString(stringData, startOffset, currentOffset - startOffset);
            strings[startOffset] = str;
            currentOffset++; // Skip null terminator
        }

        System.Diagnostics.Debug.WriteLine($"[CryXmlService] Parsed {strings.Count} strings from string table");

        // Read node table (28 bytes per entry)
        ms.Position = nodeTableOffset;
        var nodes = new List<CryXmlNode>();
        for (int i = 0; i < nodeTableCount; i++)
        {
            var node = new CryXmlNode
            {
                NameOffset = reader.ReadInt32(),
                ContentOffset = reader.ReadInt32(),
                AttributeCount = reader.ReadInt16(),
                ChildCount = reader.ReadInt16(),
                ParentIndex = reader.ReadInt32(),
                FirstAttributeIndex = reader.ReadInt32(),
                FirstChildIndex = reader.ReadInt32()
            };
            reader.ReadInt32(); // Reserved - skip
            nodes.Add(node);
        }

        // Read attribute table
        ms.Position = attributeTableOffset;
        var attributes = new List<CryXmlAttribute>();
        for (int i = 0; i < attributeTableCount; i++)
        {
            var attr = new CryXmlAttribute
            {
                NameOffset = reader.ReadInt32(),
                ValueOffset = reader.ReadInt32()
            };
            attributes.Add(attr);
        }

        // Read child table (indices of child nodes)
        ms.Position = childTableOffset;
        var childIndices = new List<int>();
        for (int i = 0; i < childTableCount; i++)
        {
            childIndices.Add(reader.ReadInt32());
        }

        // Build XML document
        var doc = new XmlDocument();

        if (nodes.Count == 0)
        {
            return doc;
        }

        // Create root element
        var rootXmlNode = BuildXmlNode(doc, nodes[0], nodes, attributes, childIndices, strings);
        if (rootXmlNode != null)
        {
            doc.AppendChild(rootXmlNode);
        }

        return doc;
    }

    private static XmlElement? BuildXmlNode(
        XmlDocument doc,
        CryXmlNode node,
        List<CryXmlNode> allNodes,
        List<CryXmlAttribute> allAttributes,
        List<int> childIndices,
        Dictionary<int, string> strings)
    {
        // Get node name
        if (!strings.TryGetValue(node.NameOffset, out var nodeName))
        {
            nodeName = $"unknown_{node.NameOffset}";
        }

        var element = doc.CreateElement(nodeName);

        // Add attributes
        for (int i = 0; i < node.AttributeCount; i++)
        {
            var attrIndex = node.FirstAttributeIndex + i;
            if (attrIndex >= 0 && attrIndex < allAttributes.Count)
            {
                var attr = allAttributes[attrIndex];

                if (!strings.TryGetValue(attr.NameOffset, out var attrName))
                {
                    attrName = $"attr_{attr.NameOffset}";
                }

                if (!strings.TryGetValue(attr.ValueOffset, out var attrValue))
                {
                    attrValue = "";
                }

                element.SetAttribute(attrName, attrValue);
            }
        }

        // Add content if present
        if (node.ContentOffset > 0 && strings.TryGetValue(node.ContentOffset, out var content) && !string.IsNullOrEmpty(content))
        {
            if (content.Contains('<') || content.Contains('&'))
            {
                element.AppendChild(doc.CreateCDataSection(content));
            }
            else
            {
                element.AppendChild(doc.CreateTextNode(content));
            }
        }

        // Add child nodes
        for (int i = 0; i < node.ChildCount; i++)
        {
            var childIndexPos = node.FirstChildIndex + i;
            if (childIndexPos >= 0 && childIndexPos < childIndices.Count)
            {
                var childNodeIndex = childIndices[childIndexPos];
                if (childNodeIndex >= 0 && childNodeIndex < allNodes.Count)
                {
                    var childElement = BuildXmlNode(doc, allNodes[childNodeIndex], allNodes, allAttributes, childIndices, strings);
                    if (childElement != null)
                    {
                        element.AppendChild(childElement);
                    }
                }
            }
        }

        return element;
    }

    private class CryXmlNode
    {
        public int NameOffset { get; set; }
        public int ContentOffset { get; set; }
        public short AttributeCount { get; set; }
        public short ChildCount { get; set; }
        public int ParentIndex { get; set; }
        public int FirstAttributeIndex { get; set; }
        public int FirstChildIndex { get; set; }
    }

    private class CryXmlAttribute
    {
        public int NameOffset { get; set; }
        public int ValueOffset { get; set; }
    }
}
