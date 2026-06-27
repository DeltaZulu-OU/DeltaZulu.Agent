using DeltaZulu.Agent.Profiles;

namespace DeltaZulu.Agent.Application.Abstractions;

public interface IProfileRepository
{
    ResourceProfile LoadFile(string path);

    ProfileLoadResult LoadDirectory(string path, string searchPattern = "*.yaml");
}
