using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using PartsUnlimited.WebsiteConfiguration;

namespace PartsUnlimited.Models
{
    public class DocDbSeeder : IDataSeeder
    {
        private readonly IDocDbConfiguration _configuration;
        private readonly SQLDataSeeder _sqlDataSeeder;

        public DocDbSeeder(IDocDbConfiguration configuration, SQLDataSeeder sqlDataSeeder)
        {
            _configuration = configuration;
            _sqlDataSeeder = sqlDataSeeder;
        }

        public async Task Seed(SampleData data)
        {
            //See remaining items which exist within sql.
            await _sqlDataSeeder.Seed(data, categories => CreateDocDbProducts(data, categories));            
        }

        private async Task<IEnumerable<IProduct>> CreateDocDbProducts(SampleData data, IEnumerable<Category> categories)
        {
            try
            {
                var client = _configuration.BuildClient();
                await CreateDatabaseIfNotExists(client);
                await CreateCollectionIfNotExists(client);
                return await CreateProducts(client, data, categories);
            }
            catch (DocumentClientException de)
            {
                Exception baseException = de.GetBaseException();
                Console.WriteLine("{0} error occurred: {1}, Message: {2}", de.StatusCode, de.Message, baseException.Message);
                throw;
            }
            catch (Exception e)
            {
                Exception baseException = e.GetBaseException();
                Console.WriteLine(baseException.Message);
                throw;
            }
        }

        private async Task<IEnumerable<IProduct>> CreateProducts(DocumentClient client, SampleData data, IEnumerable<Category> categories)
        {
            var collectionId = _configuration.BuildProductCollectionLink();
            List<Product> docDbProducts = client.CreateDocumentQuery<Product>(collectionId, "SELECT * FROM Products").AsEnumerable().ToList();

            if (!docDbProducts.Any())
            {
                IEnumerable<IProduct> products = data.GetProducts(categories, true);

                foreach (var prod in products)
                {
                    await client.CreateDocumentAsync(collectionId, prod);
                }

                return products;
            }

            return docDbProducts;

        }

        private async Task CreateDatabaseIfNotExists(DocumentClient client)
        {
            var databaseResponse = await client.ReadDatabaseFeedAsync();
            var database = databaseResponse.Where(d => d.Id == _configuration.DatabaseId).AsEnumerable().SingleOrDefault();
            if (database == null)
            {
                var newDb = new Database { Id = _configuration.DatabaseId };
                await client.CreateDatabaseAsync(newDb);
            }
        }

        private async Task CreateCollectionIfNotExists(DocumentClient client)
        {
            var databaseLink = _configuration.BuildDatabaseLink();
            var collection = client.CreateDocumentCollectionQuery(databaseLink).Where(c => c.Id == _configuration.CollectionId).ToArray().FirstOrDefault();
            if (collection == null)
            {
                var productCollection = new DocumentCollection {Id = _configuration.CollectionId };

                //Add indexing across all items for ordering and searching.
                productCollection.IndexingPolicy.IncludedPaths.Add(
                    new IncludedPath
                    {
                        Path = "/*",
                        Indexes = new Collection<Index>
                        {
                            new RangeIndex(DataType.String) { Precision = -1 },
                            new RangeIndex(DataType.Number) { Precision = -1 }
                        }
                    });
                
                await client.CreateDocumentCollectionAsync(databaseLink, productCollection);
            }
        }
    }
}