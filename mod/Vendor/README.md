# Vendored single-file dependencies

Drop third-party single-file libraries here so the rest of the tree stays
first-party.

## Required: SimpleJSON.cs

Download from https://github.com/Bunny83/SimpleJSON (MIT) and place `SimpleJSON.cs`
in this folder. The bridge uses it for protocol (de)serialisation
(`JSON.Parse`, `JSONObject`, `.AsInt` / `.AsFloat` / `.Value`).

It's not committed here to keep this repo carrying only code we author.
