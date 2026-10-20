namespace EduLibrary.ViewModels;

public sealed class DigitalAvailabilityViewModel
{
    public List<DigitalAssetCardViewModel> Assets { get; set; } = [];
    public bool Compact { get; set; }
}
