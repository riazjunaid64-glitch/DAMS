namespace DAMS.Application.DTOs.ProjectDtos
{
    /// <summary>One floor of a project: the PUT body row, and the floor list on the project detail.</summary>
    public class ProjectFloorDto
    {
        public int Number { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>A floor as the Floors popup shows it. A floor with units keeps its number.</summary>
    public class ProjectFloorResponseDto : ProjectFloorDto
    {
        public int UnitCount { get; set; }
    }
}
