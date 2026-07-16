using DeltaZulu.Pipeline.Core.Profiles;

namespace DeltaZulu.Pipeline.Core.Abstractions;

public interface IProfileRepository
{
    ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml");

    ResourceProfile LoadFile(string path);
}
