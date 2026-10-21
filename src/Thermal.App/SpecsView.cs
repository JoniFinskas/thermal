using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
namespace Thermal.App;
public partial class MainWindow {
    private Task<IReadOnlyList<SpecSection>>? specsTask;
    private IReadOnlyList<SpecSection>? specifications;
    private bool specsTimedOut;
    private async void EnsureSpecs(){
        if(specsTask!=null)return;
        specsTask=Task.Run(()=>HardwareSpecs.Detect(stop.Token));
        try{
            if(await Task.WhenAny(specsTask,Task.Delay(8000,stop.Token))!=specsTask){
                specsTimedOut=true;
                if(!stop.IsCancellationRequested&&page=="Specs"){layoutKey="";Render();}
            }
            specifications=await specsTask;
        }catch(OperationCanceledException){return;}
        catch(Exception){specifications=[new("Windows inventory","Info",[("Status","Inventory unavailable on this PC")])];}
        if(stop.IsCancellationRequested)return;
        if(page=="Specs"){layoutKey="";Render();}
    }
    private void RenderSpecs(StackPanel stack){
        EnsureSpecs();
        stack.Children.Add(PageHeader("PC specifications"));
        var summary=new StackPanel();
        var model=Text(baseline.CpuName,14,FontWeights.SemiBold);model.TextWrapping=TextWrapping.Wrap;summary.Children.Add(model);
        var capacity=Text(latest==null?"Memory capacity pending":$"{latest.TotalRamGb:F1} GiB usable RAM · {Environment.Is64BitOperatingSystem switch{true=>"64-bit",false=>"32-bit"}} Windows",12,ink:"Muted");capacity.Margin=new(0,6,0,0);summary.Children.Add(capacity);
        var summaryCard=Card(summary);summaryCard.Margin=new(0,0,0,10);stack.Children.Add(summaryCard);
        if(specifications==null){
            var loading=Text(specsTimedOut?"Windows inventory is taking too long. Basic specifications remain available.":"Detecting processor, graphics, memory, motherboard and storage…",12,ink:"Muted");loading.TextWrapping=TextWrapping.Wrap;stack.Children.Add(Card(loading));return;
        }
        foreach(var section in specifications){
            var content=new StackPanel();var heading=new StackPanel{Orientation=Orientation.Horizontal};
            heading.Children.Add(new PhosphorIcon{Kind=section.Icon,Width=20,Height=20,Margin=new(0,0,10,0)});
            heading.Children.Add(RoleText(section.Title,"SectionTitle",FontWeights.SemiBold));content.Children.Add(heading);
            content.Children.Add(new Border{Height=9});
            foreach(var (label,value) in section.Rows){
                if(label.Length==0){content.Children.Add(new Border{Height=8});continue;}
                var row=Columns(Px(142),Star);row.Margin=new(0,4,0,4);
                var name=Text(label,12,ink:"Muted");name.VerticalAlignment=VerticalAlignment.Top;Place(row,name,0);
                var data=Text(value,14);data.TextWrapping=TextWrapping.Wrap;data.ToolTip=value;Place(row,data,1);content.Children.Add(row);
            }
            var card=Card(content);card.Margin=new(0,0,0,12);stack.Children.Add(card);
        }
        var note=Text("Windows inventory can omit or misreport details. Storage capacity uses GiB; GPU memory is omitted because this API can report it incorrectly.",12,ink:"Muted");note.TextWrapping=TextWrapping.Wrap;summaryCard.ToolTip=note.Text;
    }
}
