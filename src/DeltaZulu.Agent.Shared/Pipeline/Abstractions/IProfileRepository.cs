using DeltaZulu.Agent.Shared.Pipeline.Profiles;

namespace DeltaZulu.Agent.Shared.Pipeline.Abstractions;

public interface IProfileRepository
{
    ResourceProfile LoadFile(string path);

    ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml");
}
