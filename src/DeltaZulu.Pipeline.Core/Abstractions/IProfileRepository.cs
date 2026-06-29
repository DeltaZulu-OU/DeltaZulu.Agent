using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IProfileRepository
{
    ResourceProfile LoadFile(string path);

    ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml");
}