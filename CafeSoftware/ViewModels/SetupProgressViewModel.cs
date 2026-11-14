using CafeSoftware.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace CafeSoftware.ViewModels;

// One line in the progress window
public partial class SetupStepLine : ObservableObject
{
    public string Title { get; }

    [ObservableProperty] private string _mark = "•";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private int _state;   // 0 waiting, 1 running, 2 done, 3 failed, 4 skipped

    public SetupStepLine(string title) => Title = title;

    public void SetRunning() { State = 1; Mark = "…"; }

    public void SetResult(StepResult result)
    {
        State = result.Ok ? 2 : 3;
        Mark = result.Ok ? "✔" : "✘";
        Detail = result.Message;
    }

    public void SetSkipped() { State = 4; Mark = "–"; Detail = "Skipped"; }
}

// Runs the setup steps one by one and reports each result
public partial class SetupProgressViewModel : ObservableObject
{
    public ObservableCollection<SetupStepLine> Lines { get; } = new();

    [ObservableProperty] private string _title = "Windows setup";
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _summary = "Working...";
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _hasProblems;

    public int ExitCode { get; private set; }

    public async Task RunAsync(SetupMode mode)
    {
        Title = mode switch
        {
            SetupMode.Activate => "Activating lockdown",
            SetupMode.Remove => "Removing lockdown",
            _ => "Removing lockdown and deleting the Customer account"
        };

        var steps = WindowsSetup.GetSteps(mode);
        foreach (var step in steps) Lines.Add(new SetupStepLine(step.Title));

        AppLog.Write($"Windows setup started: {mode}");

        // Activating stops at the first problem, so the PC is never left half set up without a clear message.
        // Removing carries on, so as much as possible is undone.
        bool stopOnFailure = mode == SetupMode.Activate;
        int failed = 0;

        for (int i = 0; i < steps.Count; i++)
        {
            var line = Lines[i];
            line.SetRunning();

            StepResult result;
            try { result = await Task.Run(steps[i].Run); }
            catch (Exception ex) { result = new StepResult(false, ex.Message); }

            AppLog.Write($"Windows setup: {steps[i].Title}: {(result.Ok ? "OK" : "FAILED")} - {result.Message}");
            line.SetResult(result);
            Progress = (i + 1) * 100.0 / steps.Count;

            if (!result.Ok)
            {
                failed++;
                if (stopOnFailure)
                {
                    for (int j = i + 1; j < steps.Count; j++) Lines[j].SetSkipped();
                    break;
                }
            }
        }

        HasProblems = failed > 0;
        ExitCode = failed == 0 ? 0 : 1;

        if (mode == SetupMode.Activate)
        {
            Summary = failed == 0
                ? "Done. Restart the PC now: it will sign in to Customer and open CafeSoftware. Restart once more so every restriction takes effect."
                : "Stopped because a step failed. Nothing after it was done. Read the red line above, fix the cause and try again.";
        }
        else
        {
            Summary = failed == 0
                ? "Done. The lockdown was removed."
                : "Finished, but some steps had problems. See the red lines above.";
        }

        AppLog.Write($"Windows setup finished: {(failed == 0 ? "no problems" : failed + " step(s) failed")}");
        IsDone = true;
    }

    public void Fail(string message)
    {
        Summary = message;
        HasProblems = true;
        ExitCode = 2;
        IsDone = true;
    }
}