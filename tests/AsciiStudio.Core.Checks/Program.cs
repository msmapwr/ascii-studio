using AsciiStudio.Core;

var passed=0;
void Check(string name,Action action){action();passed++;Console.WriteLine($"PASS {name}");}
void Assert(bool condition,string message){if(!condition)throw new Exception(message);}
byte[] pixels=[0,0,0,255,255,255,255,255,255,0,0,255,0,0,255,0];
Check("fixed grid dimensions",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=240,Rows=135});d.Validate();
    Assert(d.Width==240&&d.Height==135,"Wrong fixed grid dimensions");
});
Check("automatic aspect correction",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=240,CellAspect=.5});Assert(d.Height==120,"Wrong automatic rows");
});
Check("full HD character grid",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new(){Columns=1920,Rows=1080});d.Validate();Assert(d.Width*d.Height==2_073_600,"Full HD was truncated");
});
Check("grid boundaries rejected",()=>{
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=2001});throw new Exception("Oversized columns accepted");}catch(ArgumentOutOfRangeException){}
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=120,Rows=2001});throw new Exception("Oversized rows accepted");}catch(ArgumentOutOfRangeException){}
});
Check("tall auto grid is not silently distorted",()=>{
    try{ImageConverter.Convert(new byte[4*100],1,100,new(){Columns=240});throw new Exception("Tall image was silently clamped");}catch(ArgumentException){}
});
Check("legacy default options retain aspect",()=>{
    var d=ImageConverter.Convert(pixels,2,2,new());Assert(d.Width==120&&d.Height==60,"Legacy defaults changed");
});
Check("cancellation interrupts conversion",()=>{
    using var cts=new CancellationTokenSource();cts.Cancel();
    try{ImageConverter.Convert(pixels,2,2,new(){Columns=1920,Rows=1080},cts.Token);throw new Exception("Canceled conversion succeeded");}catch(OperationCanceledException){}
});
Check("alpha composite and density extremes",()=>{
    var d=ImageConverter.Convert([0,0,0,0],1,1,new(){Columns=8,Rows=1,Characters=" @"});Assert(d.Text==new string(' ',8),"Transparent pixels became dark");
    var black=ImageConverter.Convert([0,0,0,255],1,1,new(){Columns=8,Rows=1,Characters=" @"});Assert(black.Text==new string('@',8),"Black pixels lost density");
});
Console.WriteLine($"{passed} checks passed.");
