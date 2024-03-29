﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.ShoppingContent.v2;
using Google.Apis.ShoppingContent.v2.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Nop.Core;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Directory;
using Nop.Core.Domain.Security;
using Nop.Core.Domain.Stores;
using Nop.Core.Plugins;
using Nop.Data;
using Nop.Plugin.Feed.GoogleShopping.Data;
using Nop.Plugin.Feed.GoogleShopping.Services;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Configuration;
using Nop.Services.Directory;
using Nop.Services.Localization;
using Nop.Services.Media;
using Nop.Services.Seo;
using Nop.Services.Tax;
  


namespace Nop.Plugin.Feed.GoogleShopping
{
    public class GoogleShoppingService : BasePlugin, IMiscPlugin
    {
        #region Fields
        private readonly IDbContext _dbContext;
        private readonly IGoogleService _googleService;
        private readonly IPriceCalculationService _priceCalculationService;
        private readonly ITaxService _taxService;
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IManufacturerService _manufacturerService;
        private readonly IPictureService _pictureService;
        private readonly ICurrencyService _currencyService;
        private readonly ILanguageService _languageService;
        private readonly ISettingService _settingService;
        private readonly IWorkContext _workContext;
        private readonly IMeasureService _measureService;
        private readonly MeasureSettings _measureSettings;
        private readonly GoogleShoppingSettings _googleShoppingSettings;
        private readonly CurrencySettings _currencySettings;
        private readonly SecuritySettings _securitySettings;
        private readonly GoogleProductObjectContext _objectContext;
        private readonly IWebHelper _webHelper;
        private readonly IHostingEnvironment _hostingEnvironment;
        private readonly IUrlHelperFactory _urlHelperFactory;
        private readonly IActionContextAccessor _actionContextAccessor;

        #endregion

        #region Ctor

        public GoogleShoppingService(
            IDbContext dbContext,
        IGoogleService googleService,
            IPriceCalculationService priceCalculationService,
            ITaxService taxService, 
            IProductService productService,
            ICategoryService categoryService,
            IManufacturerService manufacturerService,
            IPictureService pictureService,
            ICurrencyService currencyService,
            ILanguageService languageService,
            ISettingService settingService,
            IWorkContext workContext,
            IMeasureService measureService,
            MeasureSettings measureSettings,
            GoogleShoppingSettings googleShoppingSettings,
            CurrencySettings currencySettings,
            SecuritySettings securitySettings,
            GoogleProductObjectContext objectContext,
            IWebHelper webHelper,
            IHostingEnvironment hostingEnvironment,
            IUrlHelperFactory urlHelperFactory,
            IActionContextAccessor actionContextAccessor)
        {
            this._dbContext = dbContext;
            this._googleService = googleService;
            this._priceCalculationService = priceCalculationService;
            this._taxService = taxService;
            this._productService = productService;
            this._categoryService = categoryService;
            this._manufacturerService = manufacturerService;
            this._pictureService = pictureService;
            this._currencyService = currencyService;
            this._languageService = languageService;
            this._settingService = settingService;
            this._workContext = workContext;
            this._measureService = measureService;
            this._measureSettings = measureSettings;
            this._googleShoppingSettings = googleShoppingSettings;
            this._currencySettings = currencySettings;
            this._securitySettings = securitySettings;
            this._objectContext = objectContext;
            this._webHelper = webHelper;
            this._hostingEnvironment = hostingEnvironment;
            this._urlHelperFactory = urlHelperFactory;
            this._actionContextAccessor = actionContextAccessor;
        }

        #endregion

        #region Utilities
        /// <summary>
        /// Removes invalid characters
        /// </summary>
        /// <param name="input">Input string</param>
        /// <param name="isHtmlEncoded">A value indicating whether input string is HTML encoded</param>
        /// <returns>Valid string</returns>
        protected virtual string StripInvalidChars(string input, bool isHtmlEncoded)
        {
            if (String.IsNullOrWhiteSpace(input))
                return input;

            //Microsoft uses a proprietary encoding (called CP-1252) for the bullet symbol and some other special characters, 
            //whereas most websites and data feeds use UTF-8. When you copy-paste from a Microsoft product into a website, 
            //some characters may appear as junk. Our system generates data feeds in the UTF-8 character encoding, 
            //which many shopping engines now require.

            //http://www.atensoftware.com/p90.php?q=182

            if (isHtmlEncoded)
                input = WebUtility.HtmlDecode(input);

            input = input.Replace("¼", "");
            input = input.Replace("½", "");
            input = input.Replace("¾", "");
            //input = input.Replace("•", "");
            //input = input.Replace("”", "");
            //input = input.Replace("“", "");
            //input = input.Replace("’", "");
            //input = input.Replace("‘", "");
            //input = input.Replace("™", "");
            //input = input.Replace("®", "");
            //input = input.Replace("°", "");
            
            if (isHtmlEncoded)
                input = WebUtility.HtmlEncode(input);

            return input;
        }

        protected virtual Currency GetUsedCurrency()
        {
            var currency = _currencyService.GetCurrencyById(_googleShoppingSettings.CurrencyId);
            if (currency == null || !currency.Published)
                currency = _currencyService.GetCurrencyById(_currencySettings.PrimaryStoreCurrencyId);
            return currency;
        }

        /// <summary>
        /// Get UrlHelper
        /// </summary>
        /// <returns>UrlHelper</returns>
        protected virtual IUrlHelper GetUrlHelper()
        {
            return _urlHelperFactory.GetUrlHelper(_actionContextAccessor.ActionContext);
        }

        /// <summary>
        /// Get HTTP protocol
        /// </summary>
        /// <returns>Protocol name as string</returns>
        protected virtual string GetHttpProtocol()
        {
            return _securitySettings.ForceSslForAllPages ? "https" : "http";
        }
        #endregion

        #region Methods

        /// <summary>
        /// Gets a configuration page URL
        /// </summary>
        public override string GetConfigurationPageUrl()
        {
            return $"{_webHelper.GetStoreLocation()}Admin/FeedGoogleShopping/Configure";
        }

        /// <summary>
        /// Generate a feed
        /// </summary>
        /// <param name="stream">Stream</param>
        /// <param name="store">Store</param>
        /// <returns>Generated feed</returns>
        public void GenerateFeed(Stream stream, Store store)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            if (store == null)
                throw new ArgumentNullException(nameof(store));

            const string googleBaseNamespace = "http://base.google.com/ns/1.0";

            var settings = new XmlWriterSettings
            {
                Encoding = Encoding.UTF8
            };
            
            //language
            var languageId = 0;
            var languages = _languageService.GetAllLanguages(storeId: store.Id);
            //if we have only one language, let's use it
            if (languages.Count == 1)
            {
                //let's use the first one
                var language = languages.FirstOrDefault();
                languageId = language != null ? language.Id : 0;
            }
            //otherwise, use the current one
            if (languageId == 0)
                languageId = _workContext.WorkingLanguage.Id;

            //we load all Google products here using one SQL request (performance optimization)
            var allGoogleProducts = _googleService.GetAll();



            //google 
            GoogleWebAuthorizationBroker.Folder = "ShoppingContent.Sample";
            var credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets
                {
                    ClientId = "584676283202-qphqjj998id40caks269cjag92dsc4rn.apps.googleusercontent.com",
                    ClientSecret = "mv3j9LBNBKuGXjM1xLCi6Tbr"
                },
                new string[] { ShoppingContentService.Scope.Content },
                "user",
                CancellationToken.None).Result;

            // Create the service.
            var service = new ShoppingContentService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "Shopping Content Arc",

            });



            using (var writer = XmlWriter.Create(stream, settings))
            {
                //Generate feed according to the following specs: http://www.google.com/support/merchants/bin/answer.py?answer=188494&expand=GB
                writer.WriteStartDocument();
                writer.WriteStartElement("rss");
                writer.WriteAttributeString("version", "2.0");
                writer.WriteAttributeString("xmlns", "g", null, googleBaseNamespace);
                writer.WriteStartElement("channel");
                writer.WriteElementString("title", "Google Base feed");
                writer.WriteElementString("link", "http://base.google.com/base/");
                writer.WriteElementString("description", "Information about products");


                var products1 = _productService.SearchProducts(storeId: store.Id, visibleIndividuallyOnly: true)
                    .Where(x=>!x.ExcludeGoogleFeed);


                var query = from p in products1
                            from pc in p.ProductCategories.Where(pc => !pc.Category.Name.Contains("Copier") && !pc.Category.Name.Contains("Copiers - New"))
                            select p;

                foreach (var product1 in products1)
                {
                    Google.Apis.ShoppingContent.v2.Data.Product productG = new Google.Apis.ShoppingContent.v2.Data.Product();
                    
                    var productsToProcess = new List<Core.Domain.Catalog.Product>();
                    productG.ContentLanguage = "EN";
                    productG.TargetCountry = "US";
                    productG.Channel = "online";
                    productG.Availability = "in stock";
                    productG.Condition = "new";
                    productG.GoogleProductCategory = "Electronics > Print, Copy, Scan & Fax Accessories > Copier Accessories";
                    productG.Gtin = product1.Gtin;



                    var legacy_Reader = _dbContext.SqlQuery<string>($"SELECT LegacyCode FROM [dbo].LegacyIds where ItemID={product1.Id}").ToList();
                    if (legacy_Reader.Count>0)
                    {
                        foreach(var id in legacy_Reader)
                        {
                            productG.Title += $" - {id}";
                        }
                    }

                    var categoryP = product1.ProductCategories.FirstOrDefault();
                    if(categoryP!=null)
                    if (categoryP.Category.Name.Contains("Toner"))
                            productG.ProductType = "Electronics > Print, Copy, Scan & Fax Accessories > Printer Accessories > Toner & Inkjet Cartridges";                    
                    else
                            productG.ProductType = "Electronics > Print, Copy, Scan & Fax Accessories";
                     


                    switch (product1.ProductType)
                    {
                        case ProductType.SimpleProduct:
                            {
                                //simple product doesn't have child products
                                productsToProcess.Add(product1);
                            }
                            break;
                        case ProductType.GroupedProduct:
                            {
                                //grouped products could have several child products
                                var associatedProducts = _productService.GetAssociatedProducts(product1.Id, store.Id);
                                productsToProcess.AddRange(associatedProducts);
                            }
                            break;
                        default:
                            continue;
                    }
                    foreach (var product in productsToProcess)
                    {
                        writer.WriteStartElement("item");

                        #region Basic Product Information

                        //id [id]- An identifier of the item
                        writer.WriteElementString("g", "id", googleBaseNamespace, product.Id.ToString());

                        //title [title] - Title of the item
                        writer.WriteStartElement("title");
                        var title = product.GetLocalized(x => x.Name, languageId);
                        //title should be not longer than 70 characters
                        if (title.Length > 70)
                            title = title.Substring(0, 70);
                        writer.WriteCData(title);
                        writer.WriteEndElement(); // title

                        //description [description] - Description of the item
                        writer.WriteStartElement("description");
                        string description = product.GetLocalized(x => x.FullDescription, languageId);
                        if (String.IsNullOrEmpty(description))
                            description = product.GetLocalized(x => x.ShortDescription, languageId);
                        if (String.IsNullOrEmpty(description))
                            description = product.GetLocalized(x => x.Name, languageId); //description is required
                        //resolving character encoding issues in your data feed
                        description = StripInvalidChars(description, true);
                        writer.WriteCData(description);
                        writer.WriteEndElement(); // description



                        //google product category [google_product_category] - Google's category of the item
                        //the category of the product according to Google’s product taxonomy. http://www.google.com/support/merchants/bin/answer.py?answer=160081
                        string googleProductCategory = "";
                        //var googleProduct = _googleService.GetByProductId(product.Id);
                        var googleProduct = allGoogleProducts.FirstOrDefault(x => x.ProductId == product.Id);
                        if (googleProduct != null)
                            googleProductCategory = googleProduct.Taxonomy;
                        if (String.IsNullOrEmpty(googleProductCategory))
                            googleProductCategory = _googleShoppingSettings.DefaultGoogleCategory;
                        if (String.IsNullOrEmpty(googleProductCategory))
                            throw new NopException("Default Google category is not set");
                        writer.WriteStartElement("g", "google_product_category", googleBaseNamespace);
                        writer.WriteCData(googleProductCategory);
                        writer.WriteFullEndElement(); // g:google_product_category

                        //product type [product_type] - Your category of the item
                        var defaultProductCategory = _categoryService
                            .GetProductCategoriesByProductId(product.Id, store.Id)
                            .FirstOrDefault();
                        if (defaultProductCategory != null)
                        {
                            //TODO localize categories
                            var category = defaultProductCategory.Category
                                .GetFormattedBreadCrumb(_categoryService, separator: ">", languageId: languageId);
                            if (!String.IsNullOrEmpty(category))
                            {
                                writer.WriteStartElement("g", "product_type", googleBaseNamespace);
                                writer.WriteCData(category);
                                writer.WriteFullEndElement(); // g:product_type
                            }
                        }

                        //link [link] - URL directly linking to your item's page on your website
                        var productUrl = GetUrlHelper().RouteUrl("Product", new { SeName = product.GetSeName(languageId) }, GetHttpProtocol());

                        productG.Link = productUrl;

                        writer.WriteElementString("link", productUrl);

                        //image link [image_link] - URL of an image of the item
                        //additional images [additional_image_link]
                        //up to 10 pictures
                        const int maximumPictures = 10;
                        var storeLocation = _securitySettings.ForceSslForAllPages ? 
                            (!string.IsNullOrWhiteSpace(store.SecureUrl) ? store.SecureUrl : store.Url.Replace("http://", "https://")): 
                            store.Url;
                        var pictures = _pictureService.GetPicturesByProductId(product.Id, maximumPictures);
                        for (int i = 0; i < pictures.Count; i++)
                        {
                            var picture = pictures[i];
                            var imageUrl = _pictureService.GetPictureUrl(picture,
                                _googleShoppingSettings.ProductPictureSize,
                                storeLocation: storeLocation);

                            if (i == 0)
                            {
                                //default image

                                productG.ImageLink = imageUrl;

                                writer.WriteElementString("g", "image_link", googleBaseNamespace, imageUrl);
                            }
                            else
                            {
                                //additional image

                                productG.AdditionalImageLinks.Add(imageUrl);

                                writer.WriteElementString("g", "additional_image_link", googleBaseNamespace, imageUrl);
                            }
                        }
                        if (!pictures.Any())
                        {
                            //no picture? submit a default one
                            var imageUrl = _pictureService.GetDefaultPictureUrl(_googleShoppingSettings.ProductPictureSize, storeLocation: storeLocation);

                            productG.ImageLink = imageUrl;

                            writer.WriteElementString("g", "image_link", googleBaseNamespace, imageUrl);
                        }

                        //condition [condition] - Condition or state of the item

                        productG.Condition = "new";
                        productG.ExpirationDate = DateTime.Now.AddDays(_googleShoppingSettings.ExpirationNumberOfDays).ToString("yyyy-MM-dd");

                        writer.WriteElementString("g", "condition", googleBaseNamespace, "new");

                        writer.WriteElementString("g", "expiration_date", googleBaseNamespace, DateTime.Now.AddDays(_googleShoppingSettings.ExpirationNumberOfDays).ToString("yyyy-MM-dd"));

                        #endregion

                        #region Availability & Price

                        //availability [availability] - Availability status of the item
                        string availability = "in stock"; //in stock by default
                        if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStock
                            && product.BackorderMode == BackorderMode.NoBackorders
                            && product.GetTotalStockQuantity() <= 0)
                        {
                            availability = "out of stock";
                        }
                        //uncomment th code below in order to support "preorder" value for "availability"
                        //if (product.AvailableForPreOrder &&
                        //    (!product.PreOrderAvailabilityStartDateTimeUtc.HasValue || 
                        //    product.PreOrderAvailabilityStartDateTimeUtc.Value >= DateTime.UtcNow))
                        //{
                        //    availability = "preorder";
                        //}

                        productG.Availability = availability;

                        writer.WriteElementString("g", "availability", googleBaseNamespace, availability);

                        //price [price] - Price of the item
                        var currency = GetUsedCurrency();
                        decimal finalPriceBase;
                        if (_googleShoppingSettings.PricesConsiderPromotions)
                        {
                            var minPossiblePrice = _priceCalculationService.GetFinalPrice(product, _workContext.CurrentCustomer);

                            if (product.HasTierPrices)
                            {
                                //calculate price for the maximum quantity if we have tier prices, and choose minimal
                                minPossiblePrice = Math.Min(minPossiblePrice,
                                    _priceCalculationService.GetFinalPrice(product, _workContext.CurrentCustomer, quantity: int.MaxValue));
                            }

                            finalPriceBase = _taxService.GetProductPrice(product, minPossiblePrice, out decimal _);
                        }
                        else
                        {
                            finalPriceBase = product.Price;
                        }
                        decimal price = _currencyService.ConvertFromPrimaryStoreCurrency(finalPriceBase, currency);
                        //round price now so it matches the product details page
                        price = RoundingHelper.RoundPrice(price);

                        Price priceG = new Price { Currency = currency.CurrencyCode, Value = price.ToString(new CultureInfo("en-US", false).NumberFormat) };

                        productG.Price= priceG;

                        writer.WriteElementString("g", "price", googleBaseNamespace,
                                                  price.ToString(new CultureInfo("en-US", false).NumberFormat) + " " +
                                                  currency.CurrencyCode);

                        #endregion

                        #region Unique Product Identifiers

                        /* Unique product identifiers such as UPC, EAN, JAN or ISBN allow us to show your listing on the appropriate product page. If you don't provide the required unique product identifiers, your store may not appear on product pages, and all your items may be removed from Product Search.
                         * We require unique product identifiers for all products - except for custom made goods. For apparel, you must submit the 'brand' attribute. For media (such as books, movies, music and video games), you must submit the 'gtin' attribute. In all cases, we recommend you submit all three attributes.
                         * You need to submit at least two attributes of 'brand', 'gtin' and 'mpn', but we recommend that you submit all three if available. For media (such as books, movies, music and video games), you must submit the 'gtin' attribute, but we recommend that you include 'brand' and 'mpn' if available.
                        */

                        //GTIN [gtin] - GTIN
                        var gtin = product.Gtin;
                        if (!String.IsNullOrEmpty(gtin))
                        {
                            productG.Gtin = gtin;

                            writer.WriteStartElement("g", "gtin", googleBaseNamespace);
                            writer.WriteCData(gtin);
                            writer.WriteFullEndElement(); // g:gtin
                        }

                        //brand [brand] - Brand of the item
                        var defaultManufacturer =
                            _manufacturerService.GetProductManufacturersByProductId((product.Id)).FirstOrDefault();
                        if (defaultManufacturer != null)
                        {
                            productG.Brand = defaultManufacturer.Manufacturer.Name;

                           writer.WriteStartElement("g", "brand", googleBaseNamespace);
                            writer.WriteCData(defaultManufacturer.Manufacturer.Name);
                            writer.WriteFullEndElement(); // g:brand
                        }


                        //mpn [mpn] - Manufacturer Part Number (MPN) of the item
                        var mpn = product.ManufacturerPartNumber;
                        if (!String.IsNullOrEmpty(mpn))
                        {
                            productG.Mpn = mpn;

                            writer.WriteStartElement("g", "mpn", googleBaseNamespace);
                            writer.WriteCData(mpn);
                            writer.WriteFullEndElement(); // g:mpn
                        }

                        //identifier exists [identifier_exists] - Submit custom goods
                        if (googleProduct != null && googleProduct.CustomGoods)
                        {
                            
                            productG.IdentifierExists = false;

                            writer.WriteElementString("g", "identifier_exists", googleBaseNamespace, "FALSE");
                        }

                        #endregion

                        #region Apparel Products

                        /* Apparel includes all products that fall under 'Apparel & Accessories' (including all sub-categories)
                         * in Google’s product taxonomy.
                        */

                        //gender [gender] - Gender of the item
                        if (googleProduct != null && !String.IsNullOrEmpty(googleProduct.Gender))
                        {
                            productG.Gender = googleProduct.Gender;

                            writer.WriteStartElement("g", "gender", googleBaseNamespace);
                            writer.WriteCData(googleProduct.Gender);
                            writer.WriteFullEndElement(); // g:gender
                        }

                        //age group [age_group] - Target age group of the item
                        if (googleProduct != null && !String.IsNullOrEmpty(googleProduct.AgeGroup))
                        {
                            productG.AgeGroup = googleProduct.AgeGroup;

                            writer.WriteStartElement("g", "age_group", googleBaseNamespace);
                            writer.WriteCData(googleProduct.AgeGroup);
                            writer.WriteFullEndElement(); // g:age_group
                        }

                        //color [color] - Color of the item
                        if (googleProduct != null && !String.IsNullOrEmpty(googleProduct.Color))
                        {
                            productG.Color = googleProduct.Color;

                            writer.WriteStartElement("g", "color", googleBaseNamespace);
                            writer.WriteCData(googleProduct.Color);
                            writer.WriteFullEndElement(); // g:color
                        }

                        //size [size] - Size of the item
                        if (googleProduct != null && !String.IsNullOrEmpty(googleProduct.Size))
                        {
                            productG.Sizes.Add(googleProduct.Size);

                            writer.WriteStartElement("g", "size", googleBaseNamespace);
                            writer.WriteCData(googleProduct.Size);
                            writer.WriteFullEndElement(); // g:size
                        }

                        #endregion

                        #region Tax & Shipping

                        //tax [tax]
                        //The tax attribute is an item-level override for merchant-level tax settings as defined in your Google Merchant Center account. This attribute is only accepted in the US, if your feed targets a country outside of the US, please do not use this attribute.
                        //IMPORTANT NOTE: Set tax in your Google Merchant Center account settings

                        //IMPORTANT NOTE: Set shipping in your Google Merchant Center account settings

                        //shipping weight [shipping_weight] - Weight of the item for shipping
                        //We accept only the following units of weight: lb, oz, g, kg.
                        if (_googleShoppingSettings.PassShippingInfoWeight)
                        {
                            string weightName;
                            var shippingWeight = product.Weight;
                            var weightSystemName = _measureService.GetMeasureWeightById(_measureSettings.BaseWeightId).SystemKeyword;
                            switch (weightSystemName)
                            {
                                case "ounce":
                                    weightName = "oz";
                                    break;
                                case "lb":
                                    weightName = "lb";
                                    break;
                                case "grams":
                                    weightName = "g";
                                    break;
                                case "kg":
                                    weightName = "kg";
                                    break;
                                default:
                                    //unknown weight 
                                    throw new Exception("Not supported weight. Google accepts the following units: lb, oz, g, kg.");
                            }
                            ProductShippingDimension psd = new ProductShippingDimension
                            {
                                Unit = weightName,
                                Value = double.Parse(shippingWeight.ToString()) 
                            };

                            productG.ShippingHeight = psd;

                            writer.WriteElementString("g", "shipping_weight", googleBaseNamespace, string.Format(CultureInfo.InvariantCulture, "{0} {1}", shippingWeight.ToString(new CultureInfo("en-US", false).NumberFormat), weightName));
                        }

                        //shipping length [shipping_length] - Length of the item for shipping
                        //shipping width [shipping_width] - Width of the item for shipping
                        //shipping height [shipping_height] - Height of the item for shipping
                        //We accept only the following units of length: in, cm
                        if (_googleShoppingSettings.PassShippingInfoDimensions)
                        {
                            string dimensionName;
                            var length = product.Length;
                            var width = product.Width;
                            var height = product.Height;
                            var dimensionSystemName = _measureService.GetMeasureDimensionById(_measureSettings.BaseDimensionId).SystemKeyword;
                            switch (dimensionSystemName)
                            {
                                case "inches":
                                    dimensionName = "in";
                                    break;
                                    //TODO support other dimensions (convert to cm)
                                default:
                                    //unknown dimension 
                                    throw new Exception("Not supported dimension. Google accepts the following units: in, cm.");
                            }

                            ProductShippingDimension psd = new ProductShippingDimension
                            {
                                Unit = dimensionName,
                                 Value = double.Parse(length.ToString())  
                            };
                            productG.ShippingLength = psd;

                              psd = new ProductShippingDimension
                            {
                                Unit = dimensionName,
                                Value = double.Parse(width.ToString())  
                              };
                            productG.ShippingWidth = psd;

                            psd = new ProductShippingDimension
                            {
                                Unit = dimensionName,
                                Value = double.Parse(height.ToString())   
                            };
                            productG.ShippingHeight = psd;

                            writer.WriteElementString("g", "shipping_length", googleBaseNamespace, string.Format(CultureInfo.InvariantCulture, "{0} {1}", length.ToString(new CultureInfo("en-US", false).NumberFormat), dimensionName));
                            writer.WriteElementString("g", "shipping_width", googleBaseNamespace, string.Format(CultureInfo.InvariantCulture, "{0} {1}", width.ToString(new CultureInfo("en-US", false).NumberFormat), dimensionName));
                            writer.WriteElementString("g", "shipping_height", googleBaseNamespace, string.Format(CultureInfo.InvariantCulture, "{0} {1}", height.ToString(new CultureInfo("en-US", false).NumberFormat), dimensionName));
                        }

                        #endregion

                        writer.WriteEndElement(); // item

                        
                        //used in in description
                        string usedinquery =$@"SELECT     ItemsCompatability.ItemID 
                        FROM         ItemsCompatability INNER JOIN
                                              Items ON ItemsCompatability.ItemID = Items.ItemID
                        WHERE     (ItemsCompatability.ItemIDPart ={product.Id})
                        UNION
                        SELECT     [Groups-Items].ItemID 
                        FROM         [Groups-Items] INNER JOIN
                                              Items AS Items_1 ON [Groups-Items].ItemID = Items_1.ItemID
                        WHERE     ([Groups-Items].GroupID IN
                                                  (SELECT     GroupID
                                                    FROM          [Relations-Groups-Items]
                                                    WHERE      (ItemID ={product.Id}) AND (Direction = 'B')))
                        UNION
                        SELECT     [Relations-Groups-Items].ItemID 
                        FROM         Items INNER JOIN
                                              [Relations-Groups-Items] ON Items.ItemID = [Relations-Groups-Items].ItemID
                        WHERE     ([Relations-Groups-Items].Direction = 'A') AND ([Relations-Groups-Items].GroupID IN
                                                  (SELECT     GroupID
                                                    FROM          [Groups-Items]
                                                    WHERE      (ItemID ={product.Id})))";


                        var usedin_Reader = _dbContext.SqlQuery<int>(usedinquery).ToList();
                        if (usedin_Reader.Count > 0)
                        {
                            var usedIn = _productService.GetProductsByIds(usedin_Reader.ToArray());

                            foreach (var item in usedIn)
                            {
                                productG.Description = $"{productG.Description} {item.Name}, ";
                            }
                            productG.Description = productG.Description.TrimEnd(',');
                            productG.Description += ".";
                        }
                        else
                            productG.Description += "N/A";
                    }
                }

                writer.WriteEndElement(); // channel
                writer.WriteEndElement(); // rss
                writer.WriteEndDocument();
            }
        }

        /// <summary>
        /// Install plugin
        /// </summary>
        public override void Install()
        {
            //settings
            var settings = new GoogleShoppingSettings
            {
                PricesConsiderPromotions = false,
                ProductPictureSize = 125,
                PassShippingInfoWeight = false,
                PassShippingInfoDimensions = false,
                StaticFileName = $"googleshopping_{CommonHelper.GenerateRandomDigitCode(10)}.xml",
                ExpirationNumberOfDays = 28
            };
            _settingService.SaveSetting(settings);
            
            //data
            _objectContext.Install();

            //locales
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Store", "Store");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Store.Hint", "Select the store that will be used to generate the feed.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Currency", "Currency");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Currency.Hint", "Select the default currency that will be used to generate the feed.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.DefaultGoogleCategory", "Default Google category");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.DefaultGoogleCategory.Hint", "The default Google category to use if one is not specified.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.General", "General");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.GeneralInstructions", "<p><ul><li>At least two unique product identifiers are required. So each of your product should have manufacturer (brand) and MPN (manufacturer part number) specified</li><li>Specify default tax values in your Google Merchant Center account settings</li><li>Specify default shipping values in your Google Merchant Center account settings</li><li>In order to get more info about required fields look at the following article <a href=\"http://www.google.com/support/merchants/bin/answer.py?answer=188494\" target=\"_blank\">http://www.google.com/support/merchants/bin/answer.py?answer=188494</a></li></ul></p>");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Generate", "Generate feed");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Override", "Override product settings");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.OverrideInstructions", "<p>You can download the list of allowed Google product category attributes <a href=\"http://www.google.com/support/merchants/bin/answer.py?answer=160081\" target=\"_blank\">here</a></p>");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoWeight", "Pass shipping info (weight)");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoWeight.Hint", "Check if you want to include shipping information (weight) in generated XML file.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoDimensions", "Pass shipping info (dimensions)");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoDimensions.Hint", "Check if you want to include shipping information (dimensions) in generated XML file.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PricesConsiderPromotions", "Prices consider promotions");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.PricesConsiderPromotions.Hint", "Check if you want prices to be calculated with promotions (tier prices, discounts, special prices, tax, etc). But please note that it can significantly reduce time required to generate the feed file.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.ProductPictureSize", "Product thumbnail image size");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.ProductPictureSize.Hint", "The default size (pixels) for product thumbnail images.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.ProductName", "Product");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.GoogleCategory", "Google Category");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Gender", "Gender");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.AgeGroup", "Age group");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Color", "Color");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Size", "Size");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.CustomGoods", "Custom goods (no identifier exists)");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.SuccessResult", "Google Shopping feed has been successfully generated.");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.StaticFilePath", "Generated file path (static)");
            this.AddOrUpdatePluginLocaleResource("Plugins.Feed.GoogleShopping.StaticFilePath.Hint", "A file path of the generated file. It's static for your store and can be shared with the Google Shopping service.");
            
            base.Install();
        }

        /// <summary>
        /// Uninstall plugin
        /// </summary>
        public override void Uninstall()
        {
            //settings
            _settingService.DeleteSetting<GoogleShoppingSettings>();

            //data
            _objectContext.Uninstall();

            //locales
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Store");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Store.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Currency");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Currency.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.DefaultGoogleCategory");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.DefaultGoogleCategory.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.General");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.GeneralInstructions");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Generate");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Override");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.OverrideInstructions");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoWeight");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoWeight.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoDimensions");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PassShippingInfoDimensions.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PricesConsiderPromotions");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.PricesConsiderPromotions.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.ProductPictureSize");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.ProductPictureSize.Hint");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.ProductName");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.GoogleCategory");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Gender");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.AgeGroup");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Color");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.Size");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.Products.CustomGoods");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.SuccessResult");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.StaticFilePath");
            this.DeletePluginLocaleResource("Plugins.Feed.GoogleShopping.StaticFilePath.Hint");

            base.Uninstall();
        }
        
        /// <summary>
        /// Generate a static feed file
        /// </summary>
        /// <param name="store">Store</param>
        public virtual void GenerateStaticFile(Store store)
        {
            if (store == null)
                throw new ArgumentNullException(nameof(store));
            string filePath = Path.Combine(_hostingEnvironment.WebRootPath, "files\\exportimport", store.Id + "-" + _googleShoppingSettings.StaticFileName);
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
            {
                GenerateFeed(fs, store);
            }
        }

        #endregion
    }
}