using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Thermal.Core;
namespace Thermal.App;
public partial class MainWindow {
    private readonly bool acerModel=AcerModel.Qualified();
    private LocalAcerReaderClient? acerReader;
    private bool acerEnabled,acerChangeBusy,acerStartupAttempted;
    private bool IsVerification=>Environment.GetCommandLineArgs().Any(a=>a.StartsWith("--verify-",StringComparison.Ordinal));
    private void RequestAcerOnOpen(){
        // One request per ordinary app launch; never elevate from QA or retry a cancellation.
        if(!BuildPolicy.RequestAcerOnOpen||acerStartupAttempted||IsVerification||!acerModel||!LocalAcerReaderHost.Enabled)return;
        acerStartupAttempted=true;
        ToggleAcer();
    }
    private async void ToggleAcer(){
        if(IsVerification||acerChangeBusy)return;
        if(!LocalAcerReaderHost.Enabled){layoutKey="";Render();return;}
        if(acerEnabled){
            acerEnabled=false;acerChangeBusy=true;acerReader?.Dispose();layoutKey="";Render();
            try{if(acerReader!=null)await acerReader.Completion;}finally{acerChangeBusy=false;layoutKey="";Render();}
        }else{
            if(!acerModel||acerReader?.Settled==false)return;
            acerEnabled=true;acerReader=new LocalAcerReaderClient();acerReader.Pause(paused||systemSuspended);acerReader.Start();layoutKey="";Render();
        }
    }
    private void AcerPreferences(StackPanel panel){
        if(!acerModel)return;
        var action=Button(!LocalAcerReaderHost.Enabled?"Access pending":acerChangeBusy?"Stopping…":acerEnabled?"Disable":"Enable readings",ToggleAcer,"Thermometer");action.IsEnabled=!IsVerification&&!acerChangeBusy&&(acerEnabled||acerReader?.Settled!=false);
        PreferenceRow(panel,"Cpu","CPU temperature",action,LocalAcerReaderHost.Enabled?(BuildPolicy.RequestAcerOnOpen?"Windows permission is requested on open. 2-second refresh; up to 4 hours per session.":"Optional Acer readings. Enable to request Windows permission; 2-second refresh, up to 4 hours per session."):"Firmware access is disabled while safety checks finish. Available GPU, load and RAM readings continue.");
        var status=Live(()=>!LocalAcerReaderHost.Enabled?AcerAdmission.Pending:acerReader?.Status??(BuildPolicy.RequestAcerOnOpen?"CPU access is requested when Thermal opens. You can enable or disable readings here for this session.":"Off by default. Requires a qualified Acer model, BIOS and Windows reader files. Permission applies to this session only."),12,ink:"Muted");status.Margin=new(33,0,0,10);status.TextWrapping=TextWrapping.Wrap;panel.Children.Add(status);
    }
    private AcerSample? Firmware=>latest?.Acer?.FreshAt(latest.At);
    private void AcerSensors(StackPanel stack){
        if(latest?.Acer==null||latest.Acer.CpuFan.State==ReadingState.Unsupported&&latest.Acer.GpuFan.State==ReadingState.Unsupported&&latest.Acer.GpuTemperature.State==ReadingState.Unsupported)return;
        var body=new StackPanel();body.Children.Add(RoleText("Additional sensors","SectionTitle",FontWeights.SemiBold));
        void Row(string name,Func<AcerMetric?> metric,bool temperature){
            var row=Columns(Star,GridLength.Auto);row.MinHeight=36;var label=Text(name,14);Place(row,label,0);
            var value=Live(()=>{var m=metric();return m?.State==ReadingState.Available?(temperature?Temp(m.Value):$"{m.Value:N0} RPM"):m?.State==ReadingState.Stale?"Stale":m?.State==ReadingState.Unsupported?"Not supported":"Unavailable";},14,FontWeights.SemiBold);
            value.Margin=new(12,0,0,0);Place(row,value,1);body.Children.Add(row);
        }
        if(latest!.Acer!.CpuFan.State!=ReadingState.Unsupported)Row("CPU fan",()=>Firmware?.CpuFan,false);if(latest.Acer.GpuFan.State!=ReadingState.Unsupported)Row("GPU fan",()=>Firmware?.GpuFan,false);if(latest.Acer.GpuTemperature.State!=ReadingState.Unsupported)Row("GPU board temperature",()=>Firmware?.GpuTemperature,true);
        var card=Card(body);card.Margin=new(0,12,0,0);stack.Children.Add(card);
    }
}
