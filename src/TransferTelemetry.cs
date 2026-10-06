using System;
using System.Collections.Generic;
using System.Linq;

namespace PlaylistFlac
{
    // Rates are observations of progress callbacks, not socket measurements.
    // Unidentified job IDs must never be assigned to a track by arrival order.
    internal sealed class TransferTelemetry
    {
        private sealed class Sample { internal long Bytes; internal DateTime Time, Moving; internal double Rate; }
        private readonly Dictionary<string,Sample> samples=new Dictionary<string,Sample>();
        internal double Observe(string id,long bytes,long total,DateTime utc)
        {
            if(String.IsNullOrEmpty(id) || bytes<0 || total<0 || (total>0 && bytes>total))return 0;
            Sample sample;
            if(!samples.TryGetValue(id,out sample)) {samples[id]=new Sample {Bytes=bytes,Time=utc};return 0;}
            double seconds=(utc-sample.Time).TotalSeconds;
            if(seconds<=0)return sample.Rate;
            long difference=bytes-sample.Bytes;
            // Resume offsets and retry resets establish a new baseline.
            if(difference<0) {sample.Rate=0;sample.Moving=DateTime.MinValue;}
            else if(difference>0) {sample.Rate=difference/seconds;sample.Moving=utc;}
            else sample.Rate=0;
            sample.Bytes=bytes;sample.Time=utc;
            return sample.Rate;
        }
        internal void Finish(string id) {if(!String.IsNullOrEmpty(id))samples.Remove(id);}
        internal double Speed(DateTime utc) {return samples.Values.Where(s=>(utc-s.Moving).TotalSeconds<=4).Sum(s=>s.Rate);}
        internal int Moving(DateTime utc) {return samples.Values.Count(s=>s.Rate>0 && (utc-s.Moving).TotalSeconds<=4);}
        internal void Clear() {samples.Clear();}
        internal static string FormatRate(double bytes) {return bytes>=1048576 ? (bytes/1048576).ToString("0.00")+" MiB/s" : (Math.Max(0,bytes)/1024).ToString("0.0")+" KiB/s";}
    }
}
