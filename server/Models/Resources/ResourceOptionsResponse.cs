using Server.Models.ResourceTemplates;

namespace Server.Models.Resources;

public sealed class ResourceOptionsResponse
{
    public List<ResourceSpaceResponse> Spaces { get; init; } = [];
    public List<ResourceTemplateResponse> Templates { get; init; } = [];
}
