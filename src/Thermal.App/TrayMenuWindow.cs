using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Thermal.App;

public sealed class TrayMenuWindow:Window {
    private bool closing;
    internal bool HoldOpenForVerification{get;set;}
    public void Dismiss(){if(closing)return;closing=true;Close();}
    public Border Surface{get;}
    public MaterialResult Material{get;private set;}
    public TrayMenuWindow(bool glass,bool compact,Action open,Action toggleCompact,Action settings,Action exit){
        Width=230;Height=185;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
        ShowInTaskbar=false;Topmost=true;Background=Brushes.Transparent;
        var panel=new StackPanel();
        Surface=new Border{CornerRadius=new(12),BorderThickness=new(1),BorderBrush=(Brush)Application.Current.Resources["ShellEdge"],Padding=new(8),Child=panel};Content=Surface;
        void Item(string icon,string label,Action action,bool selected=false){
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(30)});row.ColumnDefinitions.Add(new());row.ColumnDefinitions.Add(new(){Width=new GridLength(18)});
            row.Children.Add(new PhosphorIcon{Kind=icon,Width=18,Height=18,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center});
            var text=new TextBlock{Text=label,FontSize=(double)Application.Current.Resources["Type.Control"],VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(text,1);row.Children.Add(text);
            if(selected){var check=new PhosphorIcon{Kind="Check",Width=15,Height=15,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(check,2);row.Children.Add(check);}
            var b=new Button{Content=row,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,Padding=new(7,8,7,8)};
            System.Windows.Automation.AutomationProperties.SetName(b,label);b.Click+=(_,_)=>{if(closing)return;Dismiss();action();};panel.Children.Add(b);
        }
        Item("Thermometer","Open Thermal",open);Item("Compact","Compact mode",toggleCompact,compact);Item("Settings","Settings",settings);
        panel.Children.Add(new Border{Height=1,Background=(Brush)Application.Current.Resources["Divider"],Margin=new(5,4,5,4)});Item("Exit","Exit",exit);
        Loaded+=(_,_)=>Material=GlassMaterial.Apply(this,Surface,glass);
        Closing+=(_,_)=>closing=true;
        Deactivated+=(_,_)=>{if(!HoldOpenForVerification)Dismiss();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Dismiss();e.Handled=true;}};
    }
}
