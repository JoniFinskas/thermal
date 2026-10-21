using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
namespace Thermal.App;

/// <summary>Phosphor regular-weight vectors. See THIRD-PARTY-NOTICES/Phosphor-MIT.txt.</summary>
public sealed class PhosphorIcon:FrameworkElement {
    private static readonly IReadOnlyDictionary<string,Geometry[]> catalog=LoadCatalog();
    internal static IEnumerable<string> AvailableKinds=>catalog.Keys;
    public string Kind{get;set;}="Cpu";
    private string ink="Ink";
    private static readonly DependencyProperty InkBrushProperty=DependencyProperty.Register("InkBrush",typeof(Brush),typeof(PhosphorIcon),new FrameworkPropertyMetadata(Brushes.White,FrameworkPropertyMetadataOptions.AffectsRender));
    public string Ink{get=>ink;set{ink=value;SetResourceReference(InkBrushProperty,value);}}
    public PhosphorIcon(){Width=20;Height=20;IsHitTestVisible=false;SetResourceReference(InkBrushProperty,"Ink");}
    private static IReadOnlyDictionary<string,Geometry[]> LoadCatalog(){
        using var stream=typeof(PhosphorIcon).Assembly.GetManifestResourceStream("Thermal.Phosphor.Regular")??throw new InvalidOperationException("Phosphor vector catalog missing");
        var paths=JsonSerializer.Deserialize<Dictionary<string,string[]>>(stream)??throw new InvalidOperationException("Invalid Phosphor catalog");
        var result=new Dictionary<string,Geometry[]>(StringComparer.Ordinal);
        foreach(var pair in paths){
            var shapes=new Geometry[pair.Value.Length];
            for(int i=0;i<shapes.Length;i++){
                var geometry=Geometry.Parse(pair.Value[i]).Clone();
                if(geometry is StreamGeometry streamGeometry)streamGeometry.FillRule=FillRule.Nonzero;
                else if(geometry is PathGeometry pathGeometry)pathGeometry.FillRule=FillRule.Nonzero;
                geometry.Freeze();shapes[i]=geometry;
            }
            result.Add(pair.Key,shapes);
        }
        return result;
    }
    protected override void OnRender(DrawingContext dc){
        base.OnRender(dc);
        if(!catalog.TryGetValue(Kind,out var shapes))throw new InvalidOperationException("Unmapped Phosphor icon: "+Kind);
        double scale=Math.Min(ActualWidth,ActualHeight)/256;
        dc.PushTransform(new TranslateTransform((ActualWidth-256*scale)/2,(ActualHeight-256*scale)/2));
        dc.PushTransform(new ScaleTransform(scale,scale));
        var brush=(Brush)GetValue(InkBrushProperty);foreach(var shape in shapes)dc.DrawGeometry(brush,null,shape);
        dc.Pop();dc.Pop();
    }
}
