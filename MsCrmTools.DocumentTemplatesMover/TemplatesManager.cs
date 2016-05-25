using DocumentFormat.OpenXml.Packaging;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace MsCrmTools.DocumentTemplatesMover
{
    internal class TemplatesManager
    {
        public int? GetEntityTypeCode(IOrganizationService service, string entity)
        {
            RetrieveEntityRequest request = new RetrieveEntityRequest();

            request.LogicalName = entity;
            request.EntityFilters = EntityFilters.Entity;

            RetrieveEntityResponse response = (RetrieveEntityResponse)service.Execute(request);
            EntityMetadata metadata = response.EntityMetadata;

            return metadata.ObjectTypeCode;
        }

        public List<Entity> GetTemplates(IOrganizationService service)
        {
            QueryExpression qe = new QueryExpression("documenttemplate") { ColumnSet = new ColumnSet("content", "name", "associatedentitytypecode", "documenttype", "clientdata") };
            qe.Criteria.AddCondition("documenttype", ConditionOperator.Equal, 2); // only word docs
            qe.Criteria.AddCondition("createdbyname", ConditionOperator.NotEqual, "SYSTEM");

            var results = service.RetrieveMultiple(qe);
            if (results != null && results.Entities != null && results.Entities.Count > 0)
            {
                return results.Entities.ToList();
            }

            return new List<Entity>();
        }

        public void ReRouteEtcViaOpenXML(Entity template, string name, string etc, int? oldEtc, int? newEtc)
        {
            byte[] content = Convert.FromBase64String(template.GetAttributeValue<string>("content"));
            MemoryStream contentStream = new MemoryStream(content);

            string toFind = string.Format("{0}/{1}", etc, oldEtc);
            string replaceWith = string.Format("{0}/{1}", etc, newEtc);

            using (var doc = WordprocessingDocument.Open(contentStream, true, new OpenSettings { AutoSave = true }))
            {
                // crm keeps the etc in multiple places; parts here are the actual merge fields
                doc.MainDocumentPart.Document.InnerXml = doc.MainDocumentPart.Document.InnerXml.Replace(toFind, replaceWith);

                // next is the actual namespace declaration
                doc.MainDocumentPart.CustomXmlParts.ToList().ForEach(a =>
                {
                    using (StreamReader reader = new StreamReader(a.GetStream()))
                    {
                        var xml = XDocument.Load(reader);

                        // crappy way to replace the xml, couldn't be bothered figuring out xml root attribute replacement...
                        var crappy = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + xml.ToString();

                        if (crappy.IndexOf(toFind) > -1) // only replace what is needed
                        {
                            crappy = crappy.Replace(toFind, replaceWith);

                            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(crappy)))
                            {
                                a.FeedData(stream);
                            }
                        }
                    }
                });
            }

            template["content"] = Convert.ToBase64String(contentStream.ToArray());
        }

        public Guid TemplateExists(IOrganizationService service, string name)
        {
            Guid result = Guid.Empty;

            QueryExpression qe = new QueryExpression("documenttemplate");
            qe.Criteria.AddCondition("status", ConditionOperator.Equal, false); // only get active templates
            qe.Criteria.AddCondition("name", ConditionOperator.Equal, name);

            var results = service.RetrieveMultiple(qe);
            if (results != null && results.Entities != null && results.Entities.Count > 0)
            {
                result = results[0].Id;
            }

            return result;
        }
    }
}