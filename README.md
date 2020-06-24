# FingerPuppet

![.NET Core](https://github.com/joskraps/FingerPuppet/workflows/.NET%20Core/badge.svg)
[![NuGet Badge](https://buildstats.info/nuget/FingerPuppet)](https://www.nuget.org/packages/FingerPuppet/)


# FingerPuppet
> WSQ decoder and fingerprint minutia matcher

This project combines two projects into a core compatible fingerprint matching library:

[SourceAFIS .net port](https://github.com/robertvazan/sourceafis-net)

[Managed.Wsq](https://github.com/grandchamp/Managed.Wsq)

Main goal was to move away from using digital persona/crossmatch backend for processing and comparing fingerprints. This does not include a WSQ encoder.

## Getting started

```java
    class Program
    {
        static void Main(string[] args)
        {
            // These are the enrollement prints
            var print1 = "wsqPrint1";
            var print2 = "wsqPrint2";
            var testList = new List<string>();
            
            //Creates FingerPrint templates from WSQs 
            var basePrint1 = ConvertToTemplate(print1);
            var basePrint2 = ConvertToTemplate(print2);

            //These represent prints that have been saved to the db (want to save the xml minutia)
            var basePrint1Xml = basePrint1.ToXml();
            var basePrint2Xml = basePrint2.ToXml();

            for (var i = 0; i < 55; i++)
            {
                testList.Add(basePrint1Xml.ToString());
                testList.Add(basePrint2Xml.ToString());
            }

            var scores = new List<double>();
            var sw = new Stopwatch();

            sw.Start();

            // This is the print that has been submitted
            var matcher = new FingerprintMatcher(basePrint1);

            foreach (var s in testList)
            {
                var t = matcher.Match(new FingerprintTemplate(XElement.Parse(s)));
                scores.Add(t);
            }
            
            sw.Stop();
            
            Console.WriteLine("Elapsed={0} testing {1} samples", sw.Elapsed, testList.Count);

            scores.ForEach(d => Console.WriteLine(d));

        }
        
        private static FingerprintTemplate ConvertToTemplate(string sample)
        {
            var decoder = new WsqDecoder();

            return new FingerprintTemplate(LoadImage(decoder.Decode(Convert.FromBase64String(sample))));
        }
```

In the code above, we take two wsq encoded prints and create finger print templates via the wsq decoder and passing the bytes to the template constructor. It takes the xml generated and adds 55 copies of each print to the list for a simple benchmark - this represents how the prints would be persisted. Note the fingerprint template also accepts the xml minutia and this is drastically faster than parsing the image data. It then loops through all prints and does a match to generate a score for compare. Thresholds will be need to be determined and tweaked per implementation.


## Features

* WSQ decoding
* Fingerprint template creation via image or xml minutia
* Fingerprint template matching


## Licensing

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
