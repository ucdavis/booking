using System.Text.Json;
using FluentAssertions;
using Server.Core.Forms;

namespace Server.Tests.Forms;

public class FormDefinitionValidatorTests
{
    [Fact]
    public void Parses_all_supported_fields_with_order_ids_options_and_rules_intact()
    {
        const string json = """
            {"fields":[
              {"id":"intro","type":"text","label":"Please read these instructions.\nThen complete the form."},
              {"id":"name","type":"input","label":"Name","helpText":"Your full name","validation":{"required":true,"minLength":2,"maxLength":200}},
              {"id":"notes","type":"textarea","label":"Notes","validation":{"required":false,"maxLength":1000}},
              {"id":"days","type":"checkboxes","label":"Days","options":[{"id":"mon","label":"Monday"},{"id":"tue","label":"Tuesday"}],"validation":{"required":true}},
              {"id":"size","type":"dropdown","label":"Size","options":[{"id":"small","label":"Small"}]},
              {"id":"kind","type":"radio","label":"Kind","options":[{"id":"small","label":"Small"}],"validation":{}}
            ]}
            """;

        var success = FormDefinitionValidator.TryParse(1, json, out var form, out var error);

        success.Should().BeTrue();
        error.Should().BeNull();
        form!.Fields.Select(field => field.Id).Should().Equal("intro", "name", "notes", "days", "size", "kind");
        form.Fields[0].Label.Should().Contain("\n");
        form.Fields[1].Validation!.Required.Should().BeTrue();
        form.Fields[1].Validation!.MinLength.Should().Be(2);
        form.Fields[1].Validation!.MaxLength.Should().Be(200);
        form.Fields[1].HelpText.Should().Be("Your full name");
        form.Fields[3].Options!.Select(option => option.Id).Should().Equal("mon", "tue");
    }

    [Fact]
    public void Empty_form_is_a_valid_draft()
    {
        FormDefinitionValidator.TryParse(1, "{\"fields\":[]}", out var form, out var error).Should().BeTrue();
        form!.Fields.Should().BeEmpty();
        error.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Rejects_unsupported_schema_versions(int version)
    {
        FormDefinitionValidator.TryParse(version, "{\"fields\":[]}", out var form, out var error).Should().BeFalse();
        form.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("{invalid}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{}")]
    [InlineData("{\"Fields\":[]}")]
    [InlineData("{\"fields\":null}")]
    [InlineData("{\"fields\":{}}")]
    [InlineData("{\"fields\":[null]}")]
    [InlineData("{\"fields\":[false]}")]
    [InlineData("{\"fields\":[{}]}")]
    [InlineData("{\"fields\":[],\"extra\":true}")]
    [InlineData("{\"fields\":[],\"fields\":[]}")]
    public void Rejects_invalid_or_ambiguous_documents_without_throwing(string? json)
    {
        FormDefinitionValidator.TryParse(1, json, out var form, out var error).Should().BeFalse();
        form.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("{\"id\":\"a\",\"type\":\"unknown\",\"label\":\"Label\"}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\"}")]
    [InlineData("{\"id\":\"a\",\"label\":\"Label\"}")]
    [InlineData("{\"type\":\"input\",\"label\":\"Label\"}")]
    [InlineData("{\"id\":\"\",\"type\":\"input\",\"label\":\"Label\"}")]
    [InlineData("{\"id\":\"a b\",\"type\":\"input\",\"label\":\"Label\"}")]
    [InlineData("{\"id\":\"a\\n\",\"type\":\"input\",\"label\":\"Label\"}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"   \"}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":null}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":42}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"helpText\":null}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"extra\":true}")]
    [InlineData("{\"id\":\"a\",\"id\":\"b\",\"type\":\"input\",\"label\":\"A\"}")]
    [InlineData("{\"id\":\"a\",\"type\":\"text\",\"label\":\"A\",\"validation\":{}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"text\",\"label\":\"A\",\"options\":[]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"options\":[]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"dropdown\",\"label\":\"A\"}")]
    [InlineData("{\"id\":\"a\",\"type\":\"checkboxes\",\"label\":\"A\",\"options\":[]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"radio\",\"label\":\"A\",\"options\":[null]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"radio\",\"label\":\"A\",\"options\":[{\"id\":\"b\",\"label\":\" \"}]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"radio\",\"label\":\"A\",\"options\":[{\"id\":\"b\",\"label\":\"B\",\"value\":\"b\"}]}")]
    [InlineData("{\"id\":\"a\",\"type\":\"radio\",\"label\":\"A\",\"options\":[{\"id\":\"b\",\"label\":\"B\"}],\"validation\":{\"maxLength\":3}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"required\":\"true\"}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"required\":null}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"pattern\":\".*\"}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"minLength\":0}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"maxLength\":-1}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"maxLength\":100001}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"minLength\":5,\"maxLength\":4}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"maxLength\":1.5}}")]
    [InlineData("{\"id\":\"a\",\"type\":\"input\",\"label\":\"A\",\"validation\":{\"maxLength\":\"5\"}}")]
    public void Rejects_invalid_fields_options_and_rules(string field)
    {
        FormDefinitionValidator.TryParse(1, "{\"fields\":[" + field + "]}", out var form, out var error).Should().BeFalse();
        form.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Rejects_duplicate_field_and_option_ids()
    {
        const string duplicateFields = """
            {"fields":[{"id":"a","type":"input","label":"A"},{"id":"a","type":"textarea","label":"B"}]}
            """;
        const string duplicateOptions = """
            {"fields":[{"id":"a","type":"dropdown","label":"A","options":[{"id":"b","label":"B"},{"id":"b","label":"C"}]}]}
            """;

        FormDefinitionValidator.TryParse(1, duplicateFields, out _, out _).Should().BeFalse();
        FormDefinitionValidator.TryParse(1, duplicateOptions, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void Enforces_field_option_and_text_limits()
    {
        var tooManyFields = JsonSerializer.Serialize(new
        {
            fields = Enumerable.Range(1, 101).Select(index => new { id = $"f{index}", type = "input", label = "Name" }),
        });
        var tooManyOptions = JsonSerializer.Serialize(new
        {
            fields = new[]
            {
                new { id = "a", type = "radio", label = "Choices", options = Enumerable.Range(1, 101)
                    .Select(index => new { id = $"o{index}", label = "Option" }) },
            },
        });
        var tooLongLabel = JsonSerializer.Serialize(new
        {
            fields = new[] { new { id = "a", type = "text", label = new string('a', 10001) } },
        });
        var tooLongHelp = JsonSerializer.Serialize(new
        {
            fields = new[] { new { id = "a", type = "input", label = "A", helpText = new string('a', 2001) } },
        });
        var tooLongId = JsonSerializer.Serialize(new
        {
            fields = new[] { new { id = new string('a', 101), type = "input", label = "A" } },
        });
        var tooLongOption = JsonSerializer.Serialize(new
        {
            fields = new[] { new { id = "a", type = "dropdown", label = "A",
                options = new[] { new { id = "b", label = new string('a', 201) } } } },
        });

        foreach (var json in new[] { tooManyFields, tooManyOptions, tooLongLabel, tooLongHelp, tooLongId, tooLongOption })
        {
            FormDefinitionValidator.TryParse(1, json, out _, out var error).Should().BeFalse();
            error.Should().NotBeNullOrWhiteSpace();
        }
        FormDefinitionValidator.TryParse(1, new string(' ', FormDefinitionValidator.MaxJsonLength) + "{}",
            out _, out _).Should().BeFalse();
    }
}
