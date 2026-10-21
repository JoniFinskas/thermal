using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Thermal.Core;
namespace Thermal.App;

public partial class MainWindow {
    private LocalCsvLog localLog=null!;
    private bool loggingRequested,loggingChangeBusy,exportBusy,closeStarted,closeReady;
    private string exportFeedback="";
    private string LoggingStatus()=>localLog.Error??(!loggingRequested?"Logging off. Enable it to save new readings.":$"Recording locally · {localLog.WrittenSamples} samples written"+(localLog.DroppedSamples>0?$" · {localLog.DroppedSamples} samples skipped (writer busy)":""));
    private async Task ChangeLogging(bool enabled){
        if(loggingChangeBusy)return;loggingChangeBusy=true;
        try{
            if(enabled)localLog.Start();else await localLog.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));
            loggingRequested=enabled;SavePreferences();
        }catch(TimeoutException){localLog.Abort();loggingRequested=false;SavePreferences();exportFeedback="Logging stopped; some queued readings could not be saved.";}
        catch(InvalidOperationException){loggingRequested=false;exportFeedback="Previous log file is still finishing. Try again shortly.";}
        finally{loggingChangeBusy=false;layoutKey="";Render();}
    }
    private async void ExportHistory(){
        if(exportBusy)return;
        var samples=history.Samples;if(samples.Count==0){exportFeedback="No retained readings to export yet.";layoutKey="";Render();return;}
        var picker=new SaveFileDialog{Title="Export retained history",Filter="CSV files (*.csv)|*.csv",DefaultExt=".csv",AddExtension=true,OverwritePrompt=true,FileName="thermal-history-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+".csv"};
        if(picker.ShowDialog(this)!=true)return;
        await ExportHistoryTo(picker.FileName,samples);
    }
    private async Task ExportHistoryTo(string path,System.Collections.Generic.IReadOnlyList<Snapshot> samples){
        exportBusy=true;exportFeedback="Saving CSV…";layoutKey="";Render();
        try{await Task.Run(()=>TelemetryCsv.ExportAsync(path,samples,stop.Token));exportFeedback="CSV saved · UTC timestamps · temperatures in °C";}
        catch(OperationCanceledException){exportFeedback="CSV export cancelled.";}
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){exportFeedback="Could not save CSV. Check the destination and available disk space.";}
        finally{exportBusy=false;layoutKey="";Render();}
    }
    private Button ExportButton(){
        var b=Button("Export CSV",ExportHistory,"Export");b.IsEnabled=!exportBusy&&history.Samples.Count>0;
        refresh.Add(()=>b.IsEnabled=!exportBusy&&history.Samples.Count>0);
        System.Windows.Automation.AutomationProperties.SetHelpText(b,"Save retained session history. Logging does not need to be enabled.");return b;
    }
    private void OpenLogFolder(){
        if(!Directory.Exists(localLog.DirectoryPath)){exportFeedback="No log folder yet. Enable logging to create it.";layoutKey="";Render();return;}
        try{Process.Start(new ProcessStartInfo{FileName=localLog.DirectoryPath,UseShellExecute=true});}
        catch(Exception ex)when(ex is System.ComponentModel.Win32Exception or InvalidOperationException){exportFeedback="Could not open the log folder.";layoutKey="";Render();}
    }
    private void LoggingPreferences(StackPanel panel){
        var toggle=new CheckBox{Style=(Style)FindResource("Toggle"),IsChecked=loggingRequested,IsEnabled=!loggingChangeBusy};
        System.Windows.Automation.AutomationProperties.SetName(toggle,"Local CSV logging");toggle.Click+=async (_,_)=>{toggle.IsEnabled=false;await ChangeLogging(toggle.IsChecked==true);};
        PreferenceRow(panel,"Storage","Local logging",toggle,"Off by default. New readings only; up to 7 days, 100 MiB and 10 files.");
        var status=Live(LoggingStatus,12,ink:"Muted");status.TextWrapping=TextWrapping.Wrap;status.Margin=new(33,0,0,10);panel.Children.Add(status);
        PreferenceRow(panel,"Folder","Saved log files",Button("Open folder",OpenLogFolder,"Folder"),"Stored in this Windows user’s local Thermal folder.");
        PreferenceRow(panel,"History","Export session history",ExportButton(),"CSV uses UTC timestamps and Celsius, regardless of display units.");
        var feedback=Live(()=>exportFeedback,12,ink:"Muted");feedback.TextWrapping=TextWrapping.Wrap;feedback.Margin=new(33,0,0,8);feedback.Visibility=exportFeedback.Length>0?Visibility.Visible:Visibility.Collapsed;panel.Children.Add(feedback);
    }
    private async void FinishLoggingOnClose(object? sender,System.ComponentModel.CancelEventArgs e){
        if(closeReady)return;e.Cancel=true;if(closeStarted)return;closeStarted=true;stop.Cancel();acerReader?.Dispose();
        await Task.Yield();
        try{await localLog.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));}catch(TimeoutException){localLog.Abort();}
        finally{closeReady=true;Close();}
    }
}
