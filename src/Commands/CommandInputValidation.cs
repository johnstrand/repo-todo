namespace RepoTodo.Commands;

internal static class CommandInputValidation
{
    public static bool TryParsePositiveId(string value, string description, out int id)
    {
        if (int.TryParse(value, out id) && id > 0)
        {
            return true;
        }

        Console.WriteLine($"Invalid {description}: {value}");
        return false;
    }
}
