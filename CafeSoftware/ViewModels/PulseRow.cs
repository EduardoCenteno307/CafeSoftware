using CafeSoftware.Helper;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CafeSoftware.ViewModels;

// One line of the coin table in Settings: "3 pulses = ₱5, bonus 300 s"
public partial class PulseRow : ObservableObject
{
    [ObservableProperty] private string _pulses = "";
    [ObservableProperty] private string _pesos = "";
    [ObservableProperty] private string _bonus = "0";
    [ObservableProperty] private string _summary = "";   // "= 35 min": what this coin gives in total

    // Works out the total time for this row from the current seconds-per-peso
    public void Refresh(int? secondsPerPeso)
    {
        bool bonusOk = string.IsNullOrWhiteSpace(Bonus) || (int.TryParse(Bonus, out int b0) && b0 >= 0);

        if (secondsPerPeso is null || !bonusOk ||
            !int.TryParse(Pesos, out int pesos) || pesos < 1)
        {
            Summary = "";
            return;
        }

        int bonus = string.IsNullOrWhiteSpace(Bonus) ? 0 : int.Parse(Bonus);
        Summary = "= " + TimeFormat.Short(pesos * secondsPerPeso.Value + bonus);
    }
}