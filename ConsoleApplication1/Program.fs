open FSharp.Data
open Types
open Search
open System


[<EntryPoint>]
let main argv = 

    let request = { 
        FlightDatesToSearch = Search.GetPotentialFlightDates(new DateTime(2015, 1, 20), new DateTime(2015, 3, 21)) |> List.ofSeq
        MaxFlightTime = new TimeSpan(22, 0, 0)
        MaxStops = 2
        MaxPrice = 650m
        Destinations = ["HAN"; "SGN"]
    }
    let result = Search.FindSuitableFlights request |> Async.RunSynchronously
    printfn "%A" result
    0 // return an integer exit code
