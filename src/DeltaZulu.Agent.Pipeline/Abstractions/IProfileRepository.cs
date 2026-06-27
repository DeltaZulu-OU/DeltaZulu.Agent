using DeltaZulu.Agent.Pipeline.Profiles;

namespace DeltaZulu.Agent.Pipeline.Abstractions;

public interface IProfileRepository
{
    ResourceProfile LoadFile(string path);

    ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml");
}
