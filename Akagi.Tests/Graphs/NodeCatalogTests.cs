using Akagi.Graphs;
using System.Collections.ObjectModel;

namespace Akagi.Tests.Graphs;

public class NodeCatalogTests
{
    [Fact]
    public void Catalog_DescribesInputsPropertiesAndOutputs()
    {
        NodeCatalog catalog = NodeCatalog.Build();

        NodeCatalog.NodeTypeInfo character = catalog.Find("Character")!;
        Assert.False(character.Abstract);
        NodeCatalog.InputInfo reflectors = Assert.Single(character.Inputs, i => i.Name == "ReflectorIds");
        Assert.True(reflectors.IsArray);
        Assert.Equal("Reflector", reflectors.AcceptsType);
        Assert.Contains("MemoryConsolidationReflector", reflectors.CompatibleNodes);
        Assert.Contains(character.Properties, p => p.Name == "AllowVoice" && Equals(p.Default, false));
        Assert.DoesNotContain(character.Properties, p => p.Name is "Id" or "Dirty");
    }

    [Fact]
    public void GraphReferences_OutputTheExportedType()
    {
        NodeCatalog catalog = NodeCatalog.Build();

        Assert.Equal("Character", catalog.Find("CharacterGraphReference")!.OutputType);
        NodeCatalog.InputInfo members = catalog.Find("CharacterGroup")!.Inputs.Single(i => i.Name == "MemberCharacterIds");
        Assert.Contains("CharacterGraphReference", members.CompatibleNodes);
        Assert.Contains("Character", members.CompatibleNodes);
    }

    [Fact]
    public void Enums_ListTheirValues()
    {
        NodeCatalog.PropertyInfoModel visibility = NodeCatalog.Build().Find("CharacterGroup")!.Properties.Single(p => p.Name == "UserVisibility");

        Assert.Contains("FinalOnly", visibility.EnumValues!);
        Assert.True(visibility.FlagsEnum);
    }

    [Fact]
    public void ToJson_RoundTrips()
    {
        string json = NodeCatalog.Build().ToJson();

        Assert.Contains("akagi-node-catalog/1", json);
    }
}

public class GraphNodeUtilsTests
{
    [Fact]
    public void ResolveType_AcceptsShortAndQualifiedNames()
    {
        Assert.Equal(typeof(Akagi.Characters.Character), GraphNodeUtils.ResolveType("Character"));
        Assert.Equal(typeof(Akagi.Characters.Character), GraphNodeUtils.ResolveType("Akagi.Characters.Character"));
        Assert.Equal(typeof(Akagi.Characters.Character), GraphNodeUtils.ResolveType(typeof(Akagi.Characters.Character).AssemblyQualifiedName!));
        Assert.Null(GraphNodeUtils.ResolveType("NotAType"));
    }

    [Fact]
    public void ConvertValue_HandlesReadOnlyDictionary()
    {
        System.Text.Json.JsonElement element = System.Text.Json.JsonSerializer.SerializeToElement(new { a = "b" });

        object? value = GraphNodeUtils.ConvertValue(element, typeof(ReadOnlyDictionary<string, string>));

        ReadOnlyDictionary<string, string> dictionary = Assert.IsType<ReadOnlyDictionary<string, string>>(value);
        Assert.Equal("b", dictionary["a"]);
    }
}
