using Avro;

var outputFolder = Path.GetFullPath(Path.Join(Directory.GetCurrentDirectory(), "../../../../Contracts"));
var codeGen = new CodeGen();

var schemaFiles = Directory.GetFiles("../../../", "*.avsc")
    .Select(File.ReadAllText)
    .Select(Schema.Parse);

foreach (var schema in schemaFiles)
{
    codeGen.AddSchema(schema);
}

codeGen.GenerateCode();
codeGen.WriteTypes(outputFolder, true);

Console.WriteLine($"Contracts updated here: {outputFolder}");