using System.Text;
using System.Xml;
using Asteriq.Services;

namespace Asteriq.Tests.Services;

public class CryXmlServiceTests
{
    [Fact]
    public void IsRegularXml_WithXmlContent_ReturnsTrue()
    {
        var xmlBytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><root></root>");

        Assert.True(CryXmlService.IsRegularXml(xmlBytes));
    }

    [Fact]
    public void IsRegularXml_WithXmlNoDeclaration_ReturnsTrue()
    {
        var xmlBytes = Encoding.UTF8.GetBytes("<root><child>value</child></root>");

        Assert.True(CryXmlService.IsRegularXml(xmlBytes));
    }

    [Fact]
    public void IsRegularXml_WithCryXmlBContent_ReturnsFalse()
    {
        var cryXmlBytes = Encoding.ASCII.GetBytes("CryXmlB\0");

        Assert.False(CryXmlService.IsRegularXml(cryXmlBytes));
    }

    [Fact]
    public void IsRegularXml_WithEmptyArray_ReturnsFalse()
    {
        Assert.False(CryXmlService.IsRegularXml(Array.Empty<byte>()));
    }

    [Fact]
    public void IsRegularXml_WithNull_ReturnsFalse()
    {
        Assert.False(CryXmlService.IsRegularXml(null!));
    }

    [Fact]
    public void IsCryXmlB_WithCryXmlBHeader_ReturnsTrue()
    {
        var cryXmlBytes = Encoding.ASCII.GetBytes("CryXmlB\0additional data");

        Assert.True(CryXmlService.IsCryXmlB(cryXmlBytes));
    }

    [Fact]
    public void IsCryXmlB_WithCryXmlHeader_ReturnsTrue()
    {
        var cryXmlBytes = Encoding.ASCII.GetBytes("CryXml\0\0additional data");

        Assert.True(CryXmlService.IsCryXmlB(cryXmlBytes));
    }

    [Fact]
    public void IsCryXmlB_WithCRY3SDKHeader_ReturnsTrue()
    {
        var cryXmlBytes = Encoding.ASCII.GetBytes("CRY3SDK\0additional data");

        Assert.True(CryXmlService.IsCryXmlB(cryXmlBytes));
    }

    [Fact]
    public void IsCryXmlB_WithRegularXml_ReturnsFalse()
    {
        var xmlBytes = Encoding.UTF8.GetBytes("<root></root>");

        Assert.False(CryXmlService.IsCryXmlB(xmlBytes));
    }

    [Fact]
    public void IsCryXmlB_WithEmptyArray_ReturnsFalse()
    {
        Assert.False(CryXmlService.IsCryXmlB(Array.Empty<byte>()));
    }

    [Fact]
    public void IsCryXmlB_WithTooShortArray_ReturnsFalse()
    {
        Assert.False(CryXmlService.IsCryXmlB(new byte[] { 0x43, 0x72, 0x79 })); // "Cry" only
    }

    [Fact]
    public void IsCryXmlB_WithNull_ReturnsFalse()
    {
        Assert.False(CryXmlService.IsCryXmlB(null!));
    }

    [Fact]
    public void Deserialize_WithRegularXml_ReturnsXmlDocument()
    {
        var xmlContent = "<profile><actionmap name=\"test\"><action name=\"action1\"/></actionmap></profile>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);
        Assert.Equal("profile", doc.DocumentElement?.Name);

        var actionmap = doc.SelectSingleNode("//actionmap");
        Assert.NotNull(actionmap);
        Assert.Equal("test", actionmap.Attributes?["name"]?.Value);
    }

    [Fact]
    public void Deserialize_WithXmlDeclaration_ReturnsXmlDocument()
    {
        var xmlContent = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><child attr=\"value\">content</child></root>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);
        Assert.Equal("root", doc.DocumentElement?.Name);

        var child = doc.SelectSingleNode("//child");
        Assert.NotNull(child);
        Assert.Equal("value", child.Attributes?["attr"]?.Value);
        Assert.Equal("content", child.InnerText);
    }

    [Fact]
    public void Deserialize_WithEmptyArray_ReturnsNull()
    {
        var doc = CryXmlService.Deserialize(Array.Empty<byte>());

        Assert.Null(doc);
    }

    [Fact]
    public void Deserialize_WithNull_ReturnsNull()
    {
        var doc = CryXmlService.Deserialize(null!);

        Assert.Null(doc);
    }

    [Fact]
    public void Deserialize_WithInvalidXml_ReturnsNull()
    {
        var invalidXml = Encoding.UTF8.GetBytes("<root><unclosed>");

        var doc = CryXmlService.Deserialize(invalidXml);

        Assert.Null(doc);
    }

    [Fact]
    public void Deserialize_WithUnknownFormat_ReturnsNull()
    {
        var randomBytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };

        var doc = CryXmlService.Deserialize(randomBytes);

        Assert.Null(doc);
    }

    [Fact]
    public void DeserializeToString_WithRegularXml_ReturnsFormattedXml()
    {
        var xmlContent = "<profile><actionmap name=\"test\"/></profile>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var result = CryXmlService.DeserializeToString(xmlBytes);

        Assert.NotNull(result);
        Assert.Contains("profile", result);
        Assert.Contains("actionmap", result);
        Assert.Contains("name=\"test\"", result);
    }

    [Fact]
    public void DeserializeToString_WithInvalidData_ReturnsNull()
    {
        var invalidData = new byte[] { 0xFF, 0xFE, 0xFD };

        var result = CryXmlService.DeserializeToString(invalidData);

        Assert.Null(result);
    }

    [Fact]
    public void Deserialize_PreservesAttributes()
    {
        var xmlContent = "<root attr1=\"value1\" attr2=\"value2\" attr3=\"123\"></root>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);
        Assert.NotNull(doc.DocumentElement);
        Assert.Equal("value1", doc.DocumentElement.GetAttribute("attr1"));
        Assert.Equal("value2", doc.DocumentElement.GetAttribute("attr2"));
        Assert.Equal("123", doc.DocumentElement.GetAttribute("attr3"));
    }

    [Fact]
    public void Deserialize_PreservesNestedElements()
    {
        var xmlContent = "<profile><actionmap name=\"spaceship_movement\"><action name=\"v_strafe_forward\"><rebind input=\"js1_y\"/></action><action name=\"v_strafe_back\"/></actionmap><actionmap name=\"spaceship_weapons\"><action name=\"v_attack1\"/></actionmap></profile>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);

        var actionmaps = doc.SelectNodes("//actionmap");
        Assert.NotNull(actionmaps);
        Assert.Equal(2, actionmaps.Count);

        var actions = doc.SelectNodes("//action");
        Assert.NotNull(actions);
        Assert.Equal(3, actions.Count);

        var rebind = doc.SelectSingleNode("//rebind");
        Assert.NotNull(rebind);
        Assert.Equal("js1_y", rebind.Attributes?["input"]?.Value);
    }

    [Fact]
    public void Deserialize_HandlesUtf8Content()
    {
        var xmlContent = "<root><message>Hello, 世界! Привет!</message></root>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);
        var message = doc.SelectSingleNode("//message");
        Assert.NotNull(message);
        Assert.Equal("Hello, 世界! Привет!", message.InnerText);
    }

    [Fact]
    public void Deserialize_HandlesEmptyElements()
    {
        var xmlContent = "<profile><empty/><alsoEmpty></alsoEmpty></profile>";
        var xmlBytes = Encoding.UTF8.GetBytes(xmlContent);

        var doc = CryXmlService.Deserialize(xmlBytes);

        Assert.NotNull(doc);

        var empty = doc.SelectSingleNode("//empty");
        Assert.NotNull(empty);
        Assert.Equal("", empty.InnerText);

        var alsoEmpty = doc.SelectSingleNode("//alsoEmpty");
        Assert.NotNull(alsoEmpty);
        Assert.Equal("", alsoEmpty.InnerText);
    }
}
