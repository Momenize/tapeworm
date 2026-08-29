namespace Application.BaseClasses;

public record Result(string? Message);
public record SuccessfulResult(string? Message) : Result(Message);
public record NothingHappenedResult(string Message) : Result(Message);
public class Failure(string code, string message): Exception
{
    public string Code = code;
    public override string Message { get; } = message;
}

public class FileFailure(string code, string message) : Failure(code, message);
public class FileNotFound(string filePath) : FileFailure("1", $"File {filePath} not found!");
public class InvalidMessagesFile(string filePath) : FileFailure("2", $"File {filePath} does not contain channel messages!");
