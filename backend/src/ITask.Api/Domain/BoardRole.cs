namespace ITask.Api.Domain;

/// <summary>
/// Board-level roles (v1). Ordered so that a higher value includes the lower ones.
/// Viewers can read and comment; Editors change lists and cards; Owners manage
/// members, settings and integrations.
/// </summary>
public enum BoardRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2,
}
