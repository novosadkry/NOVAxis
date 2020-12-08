using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace NOVAxis.Modules.Jisho
{
    public class JishoJson
    {
        public string Word { get; set; }
        public string Reading { get; set; }
        public string[][] English_definitions { get; set; }

        public static IEnumerable<JishoJson> Convert(string jsonString, int numberOfEntries)
        {
            JObject mainObject = JObject.Parse(jsonString);
            JArray dataArray = (JArray)mainObject["data"];

            if (numberOfEntries > dataArray.Count)
                numberOfEntries = dataArray.Count;

            for (int i = 0; i < numberOfEntries; i++)
            {
                JishoJson json = new JishoJson();
                JObject dataObject = (JObject)dataArray[i];

                json.Word = (string)dataObject["japanese"][0]["word"];
                json.Reading = (string)dataObject["japanese"][0]["reading"];

                JArray sensesArray = (JArray)dataObject["senses"];
                json.English_definitions = new string[sensesArray.Count][];

                for (int j = 0; j < sensesArray.Count; j++)
                {
                    JArray defArray = (JArray)sensesArray[j]["english_definitions"];
                    json.English_definitions[j] = new string[defArray.Count];

                    for (int k = 0; k < defArray.Count; k++)
                    {
                        json.English_definitions[j][k] = (string)defArray[k];
                    }
                }

                yield return json;
            }
        }
    }
}