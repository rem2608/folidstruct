namespace FolidStruct.App;

/// <summary>
/// Command line, the same options as on Linux plus the template id used by the
/// File Explorer menu:
/// <code>
///   FolidStruct                                  open the template manager
///   FolidStruct --new [TEMPLATE] [--dir DIR]     ask for a project name and create it
///   FolidStruct --new --template-id ID --dir DIR the same, choosing the template by id
/// </code>
/// </summary>
public sealed class LaunchArgs
{
    public bool NewProject { get; private set; }
    public string? TemplateName { get; private set; }
    public string? TemplateId { get; private set; }
    public string? Directory { get; private set; }

    public static LaunchArgs Parse(IEnumerable<string> args)
    {
        var result = new LaunchArgs();
        var list = args.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            string? Next() => i + 1 < list.Count && !list[i + 1].StartsWith("--") ? list[++i] : null;

            switch (list[i])
            {
                case "--new":
                    result.NewProject = true;
                    result.TemplateName = Next();
                    break;
                case "--template-id":
                    result.TemplateId = Next();
                    break;
                case "--dir":
                    result.Directory = Next();
                    break;
            }
        }
        return result;
    }
}
