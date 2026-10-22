namespace PejPass.Infrastructure.Storage;

public sealed class FileMover : IFileMover
{
    public void Move(string sourcePath, string destinationPath, bool overwrite)
    {
        File.Move(sourcePath, destinationPath, overwrite);
    }
}
