namespace PejPass.Infrastructure.Storage;

public interface IFileMover
{
    void Move(string sourcePath, string destinationPath, bool overwrite);
}
