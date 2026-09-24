// xunit.v3 bringt KEIN implizites "using Xunit;" mit — auch nicht mit
// ImplicitUsings=enable. Ohne diese Zeile meldet jede neue Testdatei CS0246
// auf Fact, Theory und InlineData, was auf den ersten Blick nach einem
// kaputten Paketverweis aussieht.
global using Xunit;
