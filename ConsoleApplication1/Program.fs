open FSharp.Data
open Types
open Search
open System



[<EntryPoint>]
let main argv = 
    let request = { 
        FromDate = new DateTime(2015, 1, 25)
        ToDate = new DateTime(2015, 3, 1)
        MaxFlightTime = new TimeSpan(22, 0, 0)
        MaxStops = 2
        MaxPrice = 650m
        NumDaysBetweenFlights = [14 .. 22]
        Destinations = ["REP"; "PNH"]
    }
    let result = Search.FindSuitableFlights request |> Async.RunSynchronously
    printfn "%A" result
    0 // return an integer exit code
