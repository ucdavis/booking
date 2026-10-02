namespace Server.Core.Domain;

public enum TeamRole
{
    Admin = 1,
    Editor = 2,
    // Existing viewer memberships remain readable, but new assignments use Admin or Editor.
    Viewer = 3,
}
