using System;
using System.Collections.Generic;
using System.Linq;
using ImageResizer.ExtensionMethods;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Data;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Common;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Orders;
using Nop.Core.Domain.Shipping;
using Nop.Core.Plugins;
using Nop.Services.Catalog;
using Nop.Services.Common;
using Nop.Services.Events;
using Nop.Services.Localization;
using Nop.Services.Logging;
using Nop.Services.Orders;
using Nop.Services.Shipping.Pickup;

namespace Nop.Services.Shipping
{
    /// <summary>
    /// Shipping service
    /// </summary>
    public partial class ShippingService : IShippingService
    {
        #region Constants

        /// <summary>
        /// Key for caching
        /// </summary>
        /// <remarks>
        /// {0} : warehouse ID
        /// </remarks>
        private const string WAREHOUSES_BY_ID_KEY = "Nop.warehouse.id-{0}";
        /// <summary>
        /// Key pattern to clear cache
        /// </summary>
        private const string WAREHOUSES_PATTERN_KEY = "Nop.warehouse.";

        #endregion

        #region Fields

        private readonly IRepository<ShippingMethod> _shippingMethodRepository;
        private readonly IRepository<Warehouse> _warehouseRepository;
        private readonly ILogger _logger;
        private readonly IProductService _productService;
        private readonly IProductAttributeParser _productAttributeParser;
        private readonly ICheckoutAttributeParser _checkoutAttributeParser;
        private readonly IGenericAttributeService _genericAttributeService;
        private readonly ILocalizationService _localizationService;
        private readonly IAddressService _addressService;
        private readonly ShippingSettings _shippingSettings;
        private readonly IPluginFinder _pluginFinder;
        private readonly IStoreContext _storeContext;
        private readonly IEventPublisher _eventPublisher;
        private readonly ShoppingCartSettings _shoppingCartSettings;
        private readonly ICacheManager _cacheManager;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="shippingMethodRepository">Shipping method repository</param>
        /// <param name="warehouseRepository">Warehouse repository</param>
        /// <param name="logger">Logger</param>
        /// <param name="productService">Product service</param>
        /// <param name="productAttributeParser">Product attribute parser</param>
        /// <param name="checkoutAttributeParser">Checkout attribute parser</param>
        /// <param name="genericAttributeService">Generic attribute service</param>
        /// <param name="localizationService">Localization service</param>
        /// <param name="addressService">Address service</param>
        /// <param name="shippingSettings">Shipping settings</param>
        /// <param name="pluginFinder">Plugin finder</param>
        /// <param name="storeContext">Store context</param>
        /// <param name="eventPublisher">Event published</param>
        /// <param name="shoppingCartSettings">Shopping cart settings</param>
        /// <param name="cacheManager">Cache manager</param>
        public ShippingService(IRepository<ShippingMethod> shippingMethodRepository,
            IRepository<Warehouse> warehouseRepository,
            ILogger logger,
            IProductService productService,
            IProductAttributeParser productAttributeParser,
            ICheckoutAttributeParser checkoutAttributeParser,
            IGenericAttributeService genericAttributeService,
            ILocalizationService localizationService,
            IAddressService addressService,
            ShippingSettings shippingSettings,
            IPluginFinder pluginFinder,
            IStoreContext storeContext,
            IEventPublisher eventPublisher,
            ShoppingCartSettings shoppingCartSettings,
            ICacheManager cacheManager)
        {
            this._shippingMethodRepository = shippingMethodRepository;
            this._warehouseRepository = warehouseRepository;
            this._logger = logger;
            this._productService = productService;
            this._productAttributeParser = productAttributeParser;
            this._checkoutAttributeParser = checkoutAttributeParser;
            this._genericAttributeService = genericAttributeService;
            this._localizationService = localizationService;
            this._addressService = addressService;
            this._shippingSettings = shippingSettings;
            this._pluginFinder = pluginFinder;
            this._storeContext = storeContext;
            this._eventPublisher = eventPublisher;
            this._shoppingCartSettings = shoppingCartSettings;
            this._cacheManager = cacheManager;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Check whether there are multiple package items in the cart for the delivery
        /// </summary>
        /// <param name="items">Package items</param>
        /// <returns>True if there are multiple items; otherwise false</returns>
        protected bool AreMultipleItems(IList<GetShippingOptionRequest.PackageItem> items)
        {
            //no items
            if (!items.Any())
                return false;

            //more than one
            if (items.Count > 1)
                return true;

            //or single item
            var singleItem = items.First();

            //but quantity more than one
            if (singleItem.GetQuantity() > 1)
                return true;

            //one item with quantity is one and without attributes
            if (string.IsNullOrEmpty(singleItem.ShoppingCartItem.AttributesXml))
                return false;

            //find associated products of item
            var associatedAttributeValues = _productAttributeParser.ParseProductAttributeValues(singleItem.ShoppingCartItem.AttributesXml)
                .Where(attributeValue => attributeValue.AttributeValueType == AttributeValueType.AssociatedToProduct);

            //whether to ship associated products
            return associatedAttributeValues.Any(attributeValue =>
                _productService.GetProductById(attributeValue.AssociatedProductId)?.IsShipEnabled ?? false);

        }

        #endregion

        #region Methods

        #region Shipping rate computation methods

        /// <summary>
        /// Load active shipping rate computation methods
        /// </summary>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Shipping rate computation methods</returns>
        public virtual IList<IShippingRateComputationMethod> LoadActiveShippingRateComputationMethods(Customer customer = null, int storeId = 0)
        {
            return LoadAllShippingRateComputationMethods(customer, storeId)
                .Where(provider => _shippingSettings.ActiveShippingRateComputationMethodSystemNames
                    .Contains(provider.PluginDescriptor.SystemName, StringComparer.InvariantCultureIgnoreCase)).ToList();
        }

        /// <summary>
        /// Load shipping rate computation method by system name
        /// </summary>
        /// <param name="systemName">System name</param>
        /// <returns>Found Shipping rate computation method</returns>
        public virtual IShippingRateComputationMethod LoadShippingRateComputationMethodBySystemName(string systemName)
        {
            var descriptor = _pluginFinder.GetPluginDescriptorBySystemName<IShippingRateComputationMethod>(systemName);
            if (descriptor != null)
                return descriptor.Instance<IShippingRateComputationMethod>();

            return null;
        }

        /// <summary>
        /// Load all shipping rate computation methods
        /// </summary>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Shipping rate computation methods</returns>
        public virtual IList<IShippingRateComputationMethod> LoadAllShippingRateComputationMethods(Customer customer = null, int storeId = 0)
        {
            return _pluginFinder.GetPlugins<IShippingRateComputationMethod>(customer: customer, storeId: storeId).ToList();
        }

        #endregion

        #region Shipping methods

        /// <summary>
        /// Deletes a shipping method
        /// </summary>
        /// <param name="shippingMethod">The shipping method</param>
        public virtual void DeleteShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException(nameof(shippingMethod));

            _shippingMethodRepository.Delete(shippingMethod);

            //event notification
            _eventPublisher.EntityDeleted(shippingMethod);
        }

        /// <summary>
        /// Gets a shipping method
        /// </summary>
        /// <param name="shippingMethodId">The shipping method identifier</param>
        /// <returns>Shipping method</returns>
        public virtual ShippingMethod GetShippingMethodById(int shippingMethodId)
        {
            if (shippingMethodId == 0)
                return null;

            return _shippingMethodRepository.GetById(shippingMethodId);
        }

        /// <summary>
        /// Gets all shipping methods
        /// </summary>
        /// <param name="filterByCountryId">The country identifier to filter by</param>
        /// <returns>Shipping methods</returns>
        public virtual IList<ShippingMethod> GetAllShippingMethods(int? filterByCountryId = null)
        {
            if (filterByCountryId.HasValue && filterByCountryId.Value > 0)
            {
                var query1 = from sm in _shippingMethodRepository.Table
                             where
                             sm.RestrictedCountries.Select(c => c.Id).Contains(filterByCountryId.Value)
                             select sm.Id;

                var query2 = from sm in _shippingMethodRepository.Table
                             where !query1.Contains(sm.Id)
                             orderby sm.DisplayOrder, sm.Id
                             select sm;

                var shippingMethods = query2.ToList();
                return shippingMethods;
            }
            else
            {
                var query = from sm in _shippingMethodRepository.Table
                            orderby sm.DisplayOrder, sm.Id
                            select sm;
                var shippingMethods = query.ToList();
                return shippingMethods;
            }
        }

        /// <summary>
        /// Inserts a shipping method
        /// </summary>
        /// <param name="shippingMethod">Shipping method</param>
        public virtual void InsertShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException(nameof(shippingMethod));

            _shippingMethodRepository.Insert(shippingMethod);

            //event notification
            _eventPublisher.EntityInserted(shippingMethod);
        }

        /// <summary>
        /// Updates the shipping method
        /// </summary>
        /// <param name="shippingMethod">Shipping method</param>
        public virtual void UpdateShippingMethod(ShippingMethod shippingMethod)
        {
            if (shippingMethod == null)
                throw new ArgumentNullException(nameof(shippingMethod));

            _shippingMethodRepository.Update(shippingMethod);

            //event notification
            _eventPublisher.EntityUpdated(shippingMethod);
        }

        #endregion

        #region Warehouses

        /// <summary>
        /// Deletes a warehouse
        /// </summary>
        /// <param name="warehouse">The warehouse</param>
        public virtual void DeleteWarehouse(Warehouse warehouse)
        {
            if (warehouse == null)
                throw new ArgumentNullException(nameof(warehouse));

            _warehouseRepository.Delete(warehouse);

            //clear cache
            _cacheManager.RemoveByPattern(WAREHOUSES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityDeleted(warehouse);
        }

        /// <summary>
        /// Gets a warehouse
        /// </summary>
        /// <param name="warehouseId">The warehouse identifier</param>
        /// <returns>Warehouse</returns>
        public virtual Warehouse GetWarehouseById(int warehouseId)
        {
            if (warehouseId == 0)
                return null;

            var key = string.Format(WAREHOUSES_BY_ID_KEY, warehouseId);
            return _cacheManager.Get(key, () => _warehouseRepository.GetById(warehouseId));
        }

        /// <summary>
        /// Gets all warehouses
        /// </summary>
        /// <returns>Warehouses</returns>
        public virtual IList<Warehouse> GetAllWarehouses()
        {
            var query = from wh in _warehouseRepository.Table
                        orderby wh.Name
                        select wh;
            var warehouses = query.ToList();
            return warehouses;
        }

        /// <summary>
        /// Inserts a warehouse
        /// </summary>
        /// <param name="warehouse">Warehouse</param>
        public virtual void InsertWarehouse(Warehouse warehouse)
        {
            if (warehouse == null)
                throw new ArgumentNullException(nameof(warehouse));

            _warehouseRepository.Insert(warehouse);

            //clear cache
            _cacheManager.RemoveByPattern(WAREHOUSES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityInserted(warehouse);
        }

        /// <summary>
        /// Updates the warehouse
        /// </summary>
        /// <param name="warehouse">Warehouse</param>
        public virtual void UpdateWarehouse(Warehouse warehouse)
        {
            if (warehouse == null)
                throw new ArgumentNullException(nameof(warehouse));

            _warehouseRepository.Update(warehouse);

            //clear cache
            _cacheManager.RemoveByPattern(WAREHOUSES_PATTERN_KEY);

            //event notification
            _eventPublisher.EntityUpdated(warehouse);
        }

        #endregion

        #region Pickup points

        /// <summary>
        /// Load active pickup point providers
        /// </summary>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Pickup point providers</returns>
        public virtual IList<IPickupPointProvider> LoadActivePickupPointProviders(Customer customer = null, int storeId = 0)
        {
            return LoadAllPickupPointProviders(customer, storeId).Where(provider => _shippingSettings.ActivePickupPointProviderSystemNames
                .Contains(provider.PluginDescriptor.SystemName, StringComparer.InvariantCultureIgnoreCase)).ToList();
        }

        /// <summary>
        /// Load pickup point provider by system name
        /// </summary>
        /// <param name="systemName">System name</param>
        /// <returns>Found pickup point provider</returns>
        public virtual IPickupPointProvider LoadPickupPointProviderBySystemName(string systemName)
        {
            var descriptor = _pluginFinder.GetPluginDescriptorBySystemName<IPickupPointProvider>(systemName);
            if (descriptor != null)
                return descriptor.Instance<IPickupPointProvider>();

            return null;
        }

        /// <summary>
        /// Load all pickup point providers
        /// </summary>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Pickup point providers</returns>
        public virtual IList<IPickupPointProvider> LoadAllPickupPointProviders(Customer customer = null, int storeId = 0)
        {
            return _pluginFinder.GetPlugins<IPickupPointProvider>(customer: customer, storeId: storeId).ToList();
        }

        #endregion

        #region Workflow

        /// <summary>
        /// Gets shopping cart item weight (of one item)
        /// </summary>
        /// <param name="shoppingCartItem">Shopping cart item</param>
        /// <param name="ignoreFreeShippedItems">Whether to ignore the weight of the products marked as "Free shipping"</param>
        /// <returns>Shopping cart item weight</returns>
        public virtual decimal GetShoppingCartItemWeight(ShoppingCartItem shoppingCartItem, bool ignoreFreeShippedItems = false)
        {
            if (shoppingCartItem == null)
                throw new ArgumentNullException(nameof(shoppingCartItem));

            return GetShoppingCartItemWeight(shoppingCartItem.Product, shoppingCartItem.AttributesXml, ignoreFreeShippedItems);
        }

        /// <summary>
        /// Gets product item weight (of one item)
        /// </summary>
        /// <param name="product">Product</param>
        /// <param name="attributesXml">Selected product attributes in XML</param>
        /// <param name="ignoreFreeShippedItems">Whether to ignore the weight of the products marked as "Free shipping"</param>
        /// <returns>Item weight</returns>
        public virtual decimal GetShoppingCartItemWeight(Product product, string attributesXml, bool ignoreFreeShippedItems = false)
        {
            if (product == null)
                return decimal.Zero;

            //product weight
            var productWeight = !product.IsFreeShipping || !ignoreFreeShippedItems ? product.Weight : decimal.Zero;

            //attribute weight
            var attributesTotalWeight = decimal.Zero;

            if (_shippingSettings.ConsiderAssociatedProductsDimensions && !string.IsNullOrEmpty(attributesXml))
            {
                var attributeValues = _productAttributeParser.ParseProductAttributeValues(attributesXml);
                foreach (var attributeValue in attributeValues)
                {
                    switch (attributeValue.AttributeValueType)
                    {
                        case AttributeValueType.Simple:
                            //simple attribute
                            attributesTotalWeight += attributeValue.WeightAdjustment;
                            break;
                        case AttributeValueType.AssociatedToProduct:
                            //bundled product
                            var associatedProduct = _productService.GetProductById(attributeValue.AssociatedProductId);
                            if (associatedProduct != null && associatedProduct.IsShipEnabled && (!associatedProduct.IsFreeShipping || !ignoreFreeShippedItems))
                                attributesTotalWeight += associatedProduct.Weight * attributeValue.Quantity;
                            break;
                    }
                }
            }

            return productWeight + attributesTotalWeight;
        }

        /// <summary>
        /// Gets shopping cart weight
        /// </summary>
        /// <param name="request">Request</param>
        /// <param name="includeCheckoutAttributes">A value indicating whether we should calculate weights of selected checkotu attributes</param>
        /// <param name="ignoreFreeShippedItems">Whether to ignore the weight of the products marked as "Free shipping"</param>
        /// <returns>Total weight</returns>
        public virtual decimal GetTotalWeight(GetShippingOptionRequest request, 
            bool includeCheckoutAttributes = true, bool ignoreFreeShippedItems = false)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            
            var totalWeight = decimal.Zero;

            //shopping cart items
            foreach (var packageItem in request.Items)
                totalWeight += GetShoppingCartItemWeight(packageItem.ShoppingCartItem, ignoreFreeShippedItems) * packageItem.GetQuantity();

            //checkout attributes
            if (request.Customer != null && includeCheckoutAttributes)
            {
                var checkoutAttributesXml = request.Customer.GetAttribute<string>(SystemCustomerAttributeNames.CheckoutAttributes, _genericAttributeService, _storeContext.CurrentStore.Id);
                if (!string.IsNullOrEmpty(checkoutAttributesXml))
                {
                    var attributeValues = _checkoutAttributeParser.ParseCheckoutAttributeValues(checkoutAttributesXml);
                    foreach (var attributeValue in attributeValues)
                        totalWeight += attributeValue.WeightAdjustment;
                }
            }

            return totalWeight;
        }

        /// <summary>
        /// Get dimensions of associated products (for quantity 1)
        /// </summary>
        /// <param name="shoppingCartItem">Shopping cart item</param>
        /// <param name="width">Width</param>
        /// <param name="length">Length</param>
        /// <param name="height">Height</param>
        /// <param name="ignoreFreeShippedItems">Whether to ignore the weight of the products marked as "Free shipping"</param>
        public virtual void GetAssociatedProductDimensions(ShoppingCartItem shoppingCartItem,
            out decimal width, out decimal length, out decimal height, bool ignoreFreeShippedItems = false)
        {
            if (shoppingCartItem == null)
                throw new ArgumentNullException(nameof(shoppingCartItem));

            width = length = height = decimal.Zero;

            //don't consider associated products dimensions
            if (!_shippingSettings.ConsiderAssociatedProductsDimensions)
                return;

            //attributes
            if (string.IsNullOrEmpty(shoppingCartItem.AttributesXml))
                return;

            //bundled products (associated attributes)
            var attributeValues = _productAttributeParser.ParseProductAttributeValues(shoppingCartItem.AttributesXml)
                .Where(x => x.AttributeValueType == AttributeValueType.AssociatedToProduct).ToList();
            foreach (var attributeValue in attributeValues)
            {
                var associatedProduct = _productService.GetProductById(attributeValue.AssociatedProductId);
                if (associatedProduct != null && associatedProduct.IsShipEnabled && (!associatedProduct.IsFreeShipping || !ignoreFreeShippedItems))
                {
                    width += associatedProduct.Width * attributeValue.Quantity;
                    length += associatedProduct.Length * attributeValue.Quantity;
                    height += associatedProduct.Height * attributeValue.Quantity;
                }
            }
        }

        /// <summary>
        /// Get total dimensions
        /// </summary>
        /// <param name="packageItems">Package items</param>
        /// <param name="width">Width</param>
        /// <param name="length">Length</param>
        /// <param name="height">Height</param>
        /// <param name="ignoreFreeShippedItems">Whether to ignore the weight of the products marked as "Free shipping"</param>
        public virtual void GetDimensions(IList<GetShippingOptionRequest.PackageItem> packageItems,
            out decimal width, out decimal length, out decimal height, bool ignoreFreeShippedItems = false)
        {
            if (packageItems == null)
                throw new ArgumentNullException(nameof(packageItems));

            //calculate cube root of volume, in case if the number of items more than 1
            if (_shippingSettings.UseCubeRootMethod && AreMultipleItems(packageItems))
            {
                //find max dimensions of the shipped items
                var maxWidth = packageItems.Max(item => !item.ShoppingCartItem.Product.IsFreeShipping || !ignoreFreeShippedItems
                    ? item.ShoppingCartItem.Product.Width : decimal.Zero);
                var maxLength = packageItems.Max(item => !item.ShoppingCartItem.Product.IsFreeShipping || !ignoreFreeShippedItems
                    ? item.ShoppingCartItem.Product.Length : decimal.Zero);
                var maxHeight = packageItems.Max(item => !item.ShoppingCartItem.Product.IsFreeShipping || !ignoreFreeShippedItems
                    ? item.ShoppingCartItem.Product.Height : decimal.Zero);

                //get total volume of the shipped items
                var totalVolume = packageItems.Sum(packageItem =>
                {
                    //product volume
                    var productVolume = !packageItem.ShoppingCartItem.Product.IsFreeShipping || !ignoreFreeShippedItems ?
                        packageItem.ShoppingCartItem.Product.Width * packageItem.ShoppingCartItem.Product.Length * packageItem.ShoppingCartItem.Product.Height : decimal.Zero;

                    //associated products volume
                    if (_shippingSettings.ConsiderAssociatedProductsDimensions && !string.IsNullOrEmpty(packageItem.ShoppingCartItem.AttributesXml))
                    {
                        productVolume += _productAttributeParser.ParseProductAttributeValues(packageItem.ShoppingCartItem.AttributesXml)
                            .Where(attributeValue => attributeValue.AttributeValueType == AttributeValueType.AssociatedToProduct).Sum(attributeValue =>
                            {
                                var associatedProduct = _productService.GetProductById(attributeValue.AssociatedProductId);
                                if (associatedProduct == null || !associatedProduct.IsShipEnabled || (associatedProduct.IsFreeShipping && ignoreFreeShippedItems))
                                    return 0;

                                //adjust max dimensions
                                maxWidth = Math.Max(maxWidth, associatedProduct.Width);
                                maxLength = Math.Max(maxLength, associatedProduct.Length);
                                maxHeight = Math.Max(maxHeight, associatedProduct.Height);

                                return attributeValue.Quantity * associatedProduct.Width * associatedProduct.Length * associatedProduct.Height;
                            });
                    }

                    //total volume of item
                    return productVolume * packageItem.GetQuantity();
                });

                //set dimensions as cube root of volume
                width = length = height = Convert.ToDecimal(Math.Pow(Convert.ToDouble(totalVolume), (1.0 / 3.0)));

                //sometimes we have products with sizes like 1x1x20
                //that's why let's ensure that a maximum dimension is always preserved
                //otherwise, shipping rate computation methods can return low rates
                width = Math.Max(width, maxWidth);
                length = Math.Max(length, maxLength);
                height = Math.Max(height, maxHeight);
            }
            else
            {
                //summarize all values (very inaccurate with multiple items)
                width = length = height = decimal.Zero;
                foreach (var packageItem in packageItems)
                {
                    var productWidth = decimal.Zero;
                    var productLength = decimal.Zero;
                    var productHeight = decimal.Zero;
                    if (!packageItem.ShoppingCartItem.Product.IsFreeShipping || !ignoreFreeShippedItems)
                    {
                        productWidth = packageItem.ShoppingCartItem.Product.Width;
                        productLength = packageItem.ShoppingCartItem.Product.Length;
                        productHeight = packageItem.ShoppingCartItem.Product.Height;
                    }

                    //associated products
                    GetAssociatedProductDimensions(packageItem.ShoppingCartItem, out decimal associatedProductsWidth, out decimal associatedProductsLength, out decimal associatedProductsHeight);

                    var quantity = packageItem.GetQuantity();
                    width += (productWidth + associatedProductsWidth) * quantity;
                    length += (productLength + associatedProductsLength) * quantity;
                    height += (productHeight + associatedProductsHeight) * quantity;
                }
            }
        }

        /// <summary>
        /// Get the nearest warehouse for the specified address
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="warehouses">List of warehouses, if null all warehouses are used.</param>
        /// <returns></returns>
        public virtual Warehouse GetNearestWarehouse(Address address, IList<Warehouse> warehouses = null)
        {
            warehouses = warehouses ?? GetAllWarehouses();

            //no address specified. return any
            if (address == null)
                return warehouses.FirstOrDefault();

            //of course, we should use some better logic to find nearest warehouse
            //but we don't have a built-in geographic database which supports "distance" functionality
            //that's why we simply look for exact matches

            //find by country
            var matchedByCountry = new List<Warehouse>();
            foreach (var warehouse in warehouses)
            {
                var warehouseAddress = _addressService.GetAddressById(warehouse.AddressId);
                if (warehouseAddress != null)
                    if (warehouseAddress.CountryId == address.CountryId)
                        matchedByCountry.Add(warehouse);
            }
            //no country matches. return any
            if (!matchedByCountry.Any())
                return warehouses.FirstOrDefault();


            //find by state
            var matchedByState = new List<Warehouse>();
            foreach (var warehouse in matchedByCountry)
            {
                var warehouseAddress = _addressService.GetAddressById(warehouse.AddressId);
                if (warehouseAddress != null)
                    if (warehouseAddress.StateProvinceId == address.StateProvinceId)
                        matchedByState.Add(warehouse);
            }
            if (matchedByState.Any())
                return matchedByState.FirstOrDefault();

            //no state matches. return any
            return matchedByCountry.FirstOrDefault();
        }

        /// <summary>
        /// Create shipment packages (requests) from shopping cart
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="shippingAddress">Shipping address</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <param name="shippingFromMultipleLocations">Value indicating whether shipping is done from multiple locations (warehouses)</param>
        /// <returns>Shipment packages (requests)</returns>
        public virtual IList<GetShippingOptionRequest> CreateShippingOptionRequests(IList<ShoppingCartItem> cart,
            Address shippingAddress, int storeId, out bool shippingFromMultipleLocations)
        {
            //if we always ship from the default shipping origin, then there's only one request
            //if we ship from warehouses ("ShippingSettings.UseWarehouseLocation" enabled),
            //then there could be several requests


            //key - warehouse identifier (0 - default shipping origin)
            //value - request
            var requests = new Dictionary<int, GetShippingOptionRequest>();

            //a list of requests with products which should be shipped separately
            var separateRequests = new List<GetShippingOptionRequest>();

            foreach (var sci in cart)
            {
                if (!sci.IsShipEnabled(_productService, _productAttributeParser))
                    continue;

                var product = sci.Product;

                //TODO properly create requests for the assocated products
                if (product == null || !product.IsShipEnabled)
                {
                    var associatedProducts = _productAttributeParser.ParseProductAttributeValues(sci.AttributesXml)
                        .Where(attributeValue => attributeValue.AttributeValueType == AttributeValueType.AssociatedToProduct)
                        .Select(attributeValue => _productService.GetProductById(attributeValue.AssociatedProductId));
                    product = associatedProducts.FirstOrDefault(associatedProduct => associatedProduct != null && associatedProduct.IsShipEnabled);
                }
                if (product == null)
                    continue;

                //warehouses
                Warehouse warehouse = null;
                if (_shippingSettings.UseWarehouseLocation)
                {
                    if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStock &&
                        product.UseMultipleWarehouses)
                    {
                        var allWarehouses = new List<Warehouse>();
                        //multiple warehouses supported
                        foreach (var pwi in product.ProductWarehouseInventory)
                        {
                            //TODO validate stock quantity when backorder is not allowed?
                            var tmpWarehouse = GetWarehouseById(pwi.WarehouseId);
                            if (tmpWarehouse != null)
                                allWarehouses.Add(tmpWarehouse);
                        }
                        warehouse = GetNearestWarehouse(shippingAddress, allWarehouses);
                    }
                    else
                    {
                        //multiple warehouses are not supported
                        warehouse = GetWarehouseById(product.WarehouseId);
                    }
                }
                var warehouseId = warehouse != null ? warehouse.Id : 0;

                if (requests.ContainsKey(warehouseId) && !product.ShipSeparately)
                {
                    //add item to existing request
                    requests[warehouseId].Items.Add(new GetShippingOptionRequest.PackageItem(sci));
                }
                else
                {
                    //create a new request
                    var request = new GetShippingOptionRequest
                    {
                        //store
                        StoreId = storeId
                    };
                    //add item
                    request.Items.Add(new GetShippingOptionRequest.PackageItem(sci));
                    //customer
                    request.Customer = cart.GetCustomer();
                    //ship to
                    request.ShippingAddress = shippingAddress;
                    //ship from
                    Address originAddress = null;
                    if (warehouse != null)
                    {
                        //warehouse address
                        originAddress = _addressService.GetAddressById(warehouse.AddressId);
                        request.WarehouseFrom = warehouse;
                    }
                    if (originAddress == null)
                    {
                        //no warehouse address. in this case use the default shipping origin
                        originAddress = _addressService.GetAddressById(_shippingSettings.ShippingOriginAddressId);
                    }
                    if (originAddress != null)
                    {
                        request.CountryFrom = originAddress.Country;
                        request.StateProvinceFrom = originAddress.StateProvince;
                        request.ZipPostalCodeFrom = originAddress.ZipPostalCode;
                        request.CityFrom = originAddress.City;
                        request.AddressFrom = originAddress.Address1;
                    }

                    if (product.ShipSeparately)
                    {
                        //ship separately
                        separateRequests.Add(request);
                    }
                    else
                    {
                        //usual request
                        requests.Add(warehouseId, request);
                    }
                }
            }

            //multiple locations?
            //currently we just compare warehouses
            //but we should also consider cases when several warehouses are located in the same address
            shippingFromMultipleLocations = requests.Select(x => x.Key).Distinct().Count() > 1;


            var result = requests.Values.ToList();
            result.AddRange(separateRequests);

            return result;
        }
        public virtual IList<GetShippingOptionRequest> CreateShippingOptionRequestsShoppingCartItem(ShoppingCartItem sci,
          Address shippingAddress, int storeId, out bool shippingFromMultipleLocations)
        {
            //if we always ship from the default shipping origin, then there's only one request
            //if we ship from warehouses ("ShippingSettings.UseWarehouseLocation" enabled),
            //then there could be several requests


            //key - warehouse identifier (0 - default shipping origin)
            //value - request
            var requests = new Dictionary<int, GetShippingOptionRequest>();

            //a list of requests with products which should be shipped separately
            var separateRequests = new List<GetShippingOptionRequest>();


            if (!sci.IsShipEnabled(_productService, _productAttributeParser))
            {
                shippingFromMultipleLocations = false;
                return null;
            }
                var product = sci.Product;

                //TODO properly create requests for the assocated products
                if (product == null || !product.IsShipEnabled)
                {
                    var associatedProducts = _productAttributeParser.ParseProductAttributeValues(sci.AttributesXml)
                        .Where(attributeValue => attributeValue.AttributeValueType == AttributeValueType.AssociatedToProduct)
                        .Select(attributeValue => _productService.GetProductById(attributeValue.AssociatedProductId));
                    product = associatedProducts.FirstOrDefault(associatedProduct => associatedProduct != null && associatedProduct.IsShipEnabled);
                }
                if (product == null)
            {
                shippingFromMultipleLocations = false;
                return null;
            }

            //warehouses
            Warehouse warehouse = null;
                if (_shippingSettings.UseWarehouseLocation)
                {
                    if (product.ManageInventoryMethod == ManageInventoryMethod.ManageStock &&
                        product.UseMultipleWarehouses)
                    {
                        var allWarehouses = new List<Warehouse>();
                        //multiple warehouses supported
                        foreach (var pwi in product.ProductWarehouseInventory)
                        {
                            //TODO validate stock quantity when backorder is not allowed?
                            var tmpWarehouse = GetWarehouseById(pwi.WarehouseId);
                            if (tmpWarehouse != null)
                                allWarehouses.Add(tmpWarehouse);
                        }
                        warehouse = GetNearestWarehouse(shippingAddress, allWarehouses);
                    }
                    else
                    {
                        //multiple warehouses are not supported
                        warehouse = GetWarehouseById(product.WarehouseId);
                    }
                }
                var warehouseId = warehouse != null ? warehouse.Id : 0;

                if (requests.ContainsKey(warehouseId) && !product.ShipSeparately)
                {
                    //add item to existing request
                    requests[warehouseId].Items.Add(new GetShippingOptionRequest.PackageItem(sci));
                }
                else
                {
                    //create a new request
                    var request = new GetShippingOptionRequest
                    {
                        //store
                        StoreId = storeId
                    };
                    //add item
                    request.Items.Add(new GetShippingOptionRequest.PackageItem(sci));
                    //customer
                    request.Customer = sci.Customer;
                    //ship to
                    request.ShippingAddress = shippingAddress;
                    //ship from
                    Address originAddress = null;
                    if (warehouse != null)
                    {
                        //warehouse address
                        originAddress = _addressService.GetAddressById(warehouse.AddressId);
                        request.WarehouseFrom = warehouse;
                    }
                    if (originAddress == null)
                    {
                        //no warehouse address. in this case use the default shipping origin
                        originAddress = _addressService.GetAddressById(_shippingSettings.ShippingOriginAddressId);
                    }
                    if (originAddress != null)
                    {
                        request.CountryFrom = originAddress.Country;
                        request.StateProvinceFrom = originAddress.StateProvince;
                        request.ZipPostalCodeFrom = originAddress.ZipPostalCode;
                        request.CityFrom = originAddress.City;
                        request.AddressFrom = originAddress.Address1;
                    }

                    if (product.ShipSeparately)
                    {
                        //ship separately
                        separateRequests.Add(request);
                    }
                    else
                    {
                        //usual request
                        requests.Add(warehouseId, request);
                    }
                }
            

            //multiple locations?
            //currently we just compare warehouses
            //but we should also consider cases when several warehouses are located in the same address
            shippingFromMultipleLocations = requests.Select(x => x.Key).Distinct().Count() > 1;


            var result = requests.Values.ToList();
            result.AddRange(separateRequests);

            return result;
        }
        ///// <summary>
        /////  Gets available shipping options
        ///// </summary>
        ///// <param name="cart">Shopping cart</param>
        ///// <param name="shippingAddress">Shipping address</param>
        ///// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        ///// <param name="allowedShippingRateComputationMethodSystemName">Filter by shipping rate computation method identifier; null to load shipping options of all shipping rate computation methods</param>
        ///// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        ///// <returns>Shipping options</returns>
        //public virtual GetShippingOptionResponse GetShippingOptions(IList<ShoppingCartItem> cart,
        //    Address shippingAddress, Customer customer = null, string allowedShippingRateComputationMethodSystemName = "", 
        //    int storeId = 0)
        //{
        //    if (cart == null)
        //        throw new ArgumentNullException(nameof(cart));

        //    var result = new GetShippingOptionResponse();

        //    //create a package
        //    var shippingOptionRequests = CreateShippingOptionRequests(cart, shippingAddress, storeId, out bool shippingFromMultipleLocations);
        //    result.ShippingFromMultipleLocations = shippingFromMultipleLocations;

        //    var shippingRateComputationMethods = LoadActiveShippingRateComputationMethods(customer, storeId);
        //    //filter by system name
        //    if (!string.IsNullOrWhiteSpace(allowedShippingRateComputationMethodSystemName))
        //    {
        //        shippingRateComputationMethods = shippingRateComputationMethods
        //            .Where(srcm => allowedShippingRateComputationMethodSystemName.Equals(srcm.PluginDescriptor.SystemName, StringComparison.InvariantCultureIgnoreCase))
        //            .ToList();
        //    }
        //    if (!shippingRateComputationMethods.Any())
        //        //throw new NopException("Shipping rate computation method could not be loaded");
        //        return result;



        //    //request shipping options from each shipping rate computation methods
        //    foreach (var srcm in shippingRateComputationMethods)
        //    {
        //        //request shipping options (separately for each package-request)
        //        IList<ShippingOption> srcmShippingOptions = null;
        //        foreach (var shippingOptionRequest in shippingOptionRequests)
        //        {
        //            var getShippingOptionResponse = srcm.GetShippingOptions(shippingOptionRequest);

        //            if (getShippingOptionResponse.Success)
        //            {
        //                //success
        //                if (srcmShippingOptions == null)
        //                {
        //                    //first shipping option request
        //                    srcmShippingOptions = getShippingOptionResponse.ShippingOptions;
        //                }
        //                else
        //                {
        //                    //get shipping options which already exist for prior requested packages for this scrm (i.e. common options)
        //                    srcmShippingOptions = srcmShippingOptions
        //                        .Where(existingso => getShippingOptionResponse.ShippingOptions.Any(newso => newso.Name == existingso.Name))
        //                        .ToList();

        //                    //and sum the rates
        //                    foreach (var existingso in srcmShippingOptions)
        //                    {
        //                        existingso.Rate += getShippingOptionResponse
        //                            .ShippingOptions
        //                            .First(newso => newso.Name == existingso.Name)
        //                            .Rate;
        //                    }
        //                }
        //            }
        //            else
        //            {
        //                //errors
        //                foreach (var error in getShippingOptionResponse.Errors)
        //                {
        //                    result.AddError(error);
        //                    _logger.Warning($"Shipping ({srcm.PluginDescriptor.FriendlyName}). {error}");
        //                }
        //                //clear the shipping options in this case
        //                srcmShippingOptions = new List<ShippingOption>();
        //                break;
        //            }
        //        }

        //        //add this scrm's options to the result
        //        if (srcmShippingOptions != null)
        //        {
        //            foreach (var so in srcmShippingOptions)
        //            {
        //                //set system name if not set yet
        //                if (string.IsNullOrEmpty(so.ShippingRateComputationMethodSystemName))
        //                    so.ShippingRateComputationMethodSystemName = srcm.PluginDescriptor.SystemName;
        //                if (_shoppingCartSettings.RoundPricesDuringCalculation)
        //                    so.Rate = RoundingHelper.RoundPrice(so.Rate);

        //                var qty = cart.AsEnumerable().Sum(x => x.Quantity);

        //                if (so.ShippingRateComputationMethodSystemName.Contains("CANADA"))
        //                {
        //                    if (qty > 1)
        //                    {
        //                        so.Rate *= 0.75M;
        //                    }

        //                }
        //                else
        //                {
        //                    //if (Alternate_Discount_System(srcm.pro))
        //                    //{
        //                    //    //so.Rate *= Discount_Price(cartdata.ItemCategoryName);
        //                    //    continue;
        //                    //}


        //                    if (qty == 1)
        //                    {
        //                         so.Rate *= 1M;
        //                    }
        //                    if (qty == 2)
        //                    {
        //                         so.Rate *= 0.58M;
        //                    }
        //                    else if (qty == 3)
        //                    {
        //                         so.Rate *= 0.425M;
        //                    }
        //                    else if (qty == 4)
        //                    {
        //                         so.Rate *= 0.353M;
        //                    }
        //                    else if (qty == 5)
        //                    {
        //                         so.Rate *= 0.31M;
        //                    }
        //                    else if (qty == 6M)
        //                    {
        //                         so.Rate *= 0.28M;
        //                    }
        //                    else if (qty == 7)
        //                    {
        //                         so.Rate *= 0.26M;
        //                    }
        //                    else if (qty == 8)
        //                    {
        //                         so.Rate *= 0.253M;
        //                    }
        //                    else if (qty == 9)
        //                    {
        //                         so.Rate *= 0.245M;
        //                    }
        //                    else if (qty == 10)
        //                    {
        //                         so.Rate *= 0.24M;
        //                    }
        //                    else if (qty == 11)
        //                    {
        //                         so.Rate *= 0.235M;
        //                    }
        //                    else if (qty >= 12)
        //                    {
        //                         so.Rate *= 0.233M;
        //                    }


        //                    if (so.ShippingRateComputationMethodSystemName.Contains("Ground"))
        //                    {
        //                        so.Rate *= 0.95M;
        //                    }
        //                    else if (so.ShippingRateComputationMethodSystemName.Contains("3 Day"))
        //                    {
        //                        so.Rate *= 0.85M;
        //                    }
        //                    else if (so.ShippingRateComputationMethodSystemName.Contains("2nd Day"))
        //                    {
        //                        so.Rate *= 0.75M;
        //                    }
        //                    else if (so.ShippingRateComputationMethodSystemName.Contains("Next Day"))
        //                    {
        //                        so.Rate *= 0.68M;
        //                    }

        //                }


        //                //discount ground >=99

        //                if (so.Name.ToLower().Contains("ground"))
        //                {
        //                    var totalCart = cart.AsQueryable().Sum(x => x.Product.Price * x.Quantity);
        //                    if(totalCart>=99)
        //                       so.Rate = 0;
        //                } 



        //                result.ShippingOptions.Add(so);
        //            }
        //        }
        //    }

        //    if (_shippingSettings.ReturnValidOptionsIfThereAreAny)
        //    {
        //        //return valid options if there are any (no matter of the errors returned by other shipping rate computation methods).
        //        if (result.ShippingOptions.Any() && result.Errors.Any())
        //            result.Errors.Clear();
        //    }

        //    //no shipping options loaded
        //    if (!result.ShippingOptions.Any() && !result.Errors.Any())
        //        result.Errors.Add(_localizationService.GetResource("Checkout.ShippingOptionCouldNotBeLoaded"));

        //    return result;
        //}
        /// <summary>
        ///  Gets available shipping options
        /// </summary>
        /// <param name="cart">Shopping cart</param>
        /// <param name="shippingAddress">Shipping address</param>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="allowedShippingRateComputationMethodSystemName">Filter by shipping rate computation method identifier; null to load shipping options of all shipping rate computation methods</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Shipping options</returns>
        public virtual GetShippingOptionResponse GetShippingOptions(IList<ShoppingCartItem> cart,
            Address shippingAddress, Customer customer = null, string allowedShippingRateComputationMethodSystemName = "",
            int storeId = 0)
        {
            if (cart == null)
                throw new ArgumentNullException(nameof(cart));

            var result = new GetShippingOptionResponse();

            //create a pagackge for each item in the shopping cart
            foreach (var cartItem in cart)
            {
                var qty = cartItem.Quantity;
                cartItem.Quantity = 1;
                //create a package
                var shippingOptionRequests = CreateShippingOptionRequestsShoppingCartItem(cartItem, shippingAddress, storeId, out bool shippingFromMultipleLocations);
                result.ShippingFromMultipleLocations = shippingFromMultipleLocations;
                cartItem.Quantity = qty;
                var shippingRateComputationMethods = LoadActiveShippingRateComputationMethods(customer, storeId);
                //filter by system name
                if (!string.IsNullOrWhiteSpace(allowedShippingRateComputationMethodSystemName))
                {
                    shippingRateComputationMethods = shippingRateComputationMethods
                        .Where(srcm => allowedShippingRateComputationMethodSystemName.Equals(srcm.PluginDescriptor.SystemName, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                }
                if (!shippingRateComputationMethods.Any())
                    //throw new NopException("Shipping rate computation method could not be loaded");
                    return result;

                //request shipping options from each shipping rate computation methods
                foreach (var srcm in shippingRateComputationMethods)
                {
                    //request shipping options (separately for each package-request)
                    IList<ShippingOption> srcmShippingOptions = null;
                    foreach (var shippingOptionRequest in shippingOptionRequests)
                    {
                        var getShippingOptionResponse = srcm.GetShippingOptions(shippingOptionRequest);

                        if (getShippingOptionResponse.Success)
                        {
                            //success
                            if (srcmShippingOptions == null)
                            {
                                //first shipping option request
                                srcmShippingOptions = getShippingOptionResponse.ShippingOptions;
                            }
                            else
                            {
                                //get shipping options which already exist for prior requested packages for this scrm (i.e. common options)
                                srcmShippingOptions = srcmShippingOptions
                                    .Where(existingso => getShippingOptionResponse.ShippingOptions.Any(newso => newso.Name == existingso.Name))
                                    .ToList();

                                //and sum the rates
                                foreach (var existingso in srcmShippingOptions)
                                {
                                    existingso.Rate += getShippingOptionResponse
                                        .ShippingOptions
                                        .First(newso => newso.Name == existingso.Name)
                                        .Rate;
                                }
                            }
                        }
                        else
                        {
                            //errors
                            foreach (var error in getShippingOptionResponse.Errors)
                            {
                                result.AddError(error);
                                _logger.Warning($"Shipping ({srcm.PluginDescriptor.FriendlyName}). {error}");
                            }
                            //clear the shipping options in this case
                            srcmShippingOptions = new List<ShippingOption>();
                            break;
                        }
                    }

                    //add this scrm's options to the result
                    if (srcmShippingOptions != null)
                    {
                        foreach (var so in srcmShippingOptions)
                        {
                            //set system name if not set yet
                            if (string.IsNullOrEmpty(so.ShippingRateComputationMethodSystemName))
                                so.ShippingRateComputationMethodSystemName = srcm.PluginDescriptor.SystemName;
                            if (_shoppingCartSettings.RoundPricesDuringCalculation)
                                so.Rate = RoundingHelper.RoundPrice(so.Rate);
                           
                           var find = result.ShippingOptions.Where(x => x.Name == so.Name && x.Rate<=so.Rate).FirstOrDefault();
                            if (find == null)
                            {
                               
                                result.ShippingOptions.Add(so);
                            }
                        }
                    }
                }

                if (_shippingSettings.ReturnValidOptionsIfThereAreAny)
                {
                    //return valid options if there are any (no matter of the errors returned by other shipping rate computation methods).
                    if (result.ShippingOptions.Any() && result.Errors.Any())
                        result.Errors.Clear();
                }

                //no shipping options loaded
                if (!result.ShippingOptions.Any() && !result.Errors.Any())
                    result.Errors.Add(_localizationService.GetResource("Checkout.ShippingOptionCouldNotBeLoaded"));
            }
            //lock (_lock)
            //{
                GetCrazyDiscount(result, cart);
            //}
           

            return result;
        }
        public static readonly object _lock = new object();

        void GetCrazyDiscount(GetShippingOptionResponse result, IList<ShoppingCartItem> cart)
        {
            foreach (var so in result.ShippingOptions)
            {

                //discount ground >=99

                if (so.Name.ToLower().Contains("ground"))
                {
                    var totalCart = cart.AsQueryable().Sum(x => x.Product.Price * x.Quantity);
                    if (totalCart >= 99)
                    {
                        so.Rate = 0;
                        continue;
                    }
                }

                foreach (var cartItem in cart)
                {

                var itemCategory = string.Empty;
                if (cartItem.Product.ProductCategories.FirstOrDefault() != null)
                    itemCategory = cartItem.Product.ProductCategories.FirstOrDefault().Category.Name;

                    so.Rate = TempPrice(cartItem.Quantity, itemCategory, cart, so.Name, so.Rate);
                    //if(tempPrice!=0)
                   // so.RateWithDiscount += (so.Rate*tempPrice)* cartItem.Quantity;
                }
                //so.Rate = so.RateWithDiscount;
            }
            
        }



      decimal TempPrice(int qty, string category, IList<ShoppingCartItem> cartList, string shippingType, decimal rate, bool isCanada = false)
        {
             decimal tempprice = rate * qty;

            if (Alternate_Discount_System(category))
            {
                var price = Discount_Price(category, cartList);
                if(tempprice==0)
                      tempprice = price;
                else
                    tempprice *= price;
            }
            else
            {
                if (!isCanada)
                {
                    if (qty == 1)
                    {
                        tempprice *= 1;
                    }
                    if (qty == 2)
                    {
                        tempprice *= 0.58M;
                    }
                    else if (qty == 3)
                    {
                        tempprice *= 0.425M;
                    }
                    else if (qty == 4)
                    {
                        tempprice *= 0.353M;
                    }
                    else if (qty == 5)
                    {
                        tempprice *= 0.31M;
                    }
                    else if (qty == 6)
                    {
                        tempprice *= 0.28M;
                    }
                    else if (qty == 7)
                    {
                        tempprice *= 0.26M;
                    }
                    else if (qty == 8)
                    {
                        tempprice *= 0.253M;
                    }
                    else if (qty == 9)
                    {
                        tempprice *= 0.245M;
                    }
                    else if (qty == 10)
                    {
                        tempprice *= 0.24M;
                    }
                    else if (qty == 11)
                    {
                        tempprice *= 0.235M;
                    }
                    else if (qty >= 12)
                    {
                        tempprice *= 0.233M;
                    }
                    else if (Discount(category, cartList))
                    {
                        tempprice *= 0.75M;
                    }
                }
                else
                {
                    if (qty >1)
                    {
                        tempprice *= 0.75M;
                    }
                    else if (Discount(category, cartList))
                    {
                        tempprice *= 0.75M;
                    }

                }

            }
            if (!isCanada)
            {
                if (shippingType == "Ground")
                {
                    tempprice *= 0.95M;
                }
                else if (shippingType == "3 Day Select")
                {
                    tempprice *= 0.85M;
                }
                else if (shippingType == "2nd Day Air")
                {
                    tempprice *= 0.75M;
                }
                else if (shippingType == "Next Day Air Early A.M." || shippingType == "Next Day Air")
                {
                    tempprice *= 0.68M;
                }
                else
                {
                }
            }
             

            return tempprice;
        }
        bool Discount(string category, IList<ShoppingCartItem> cartList)
        {
            int count = 0;

           

            foreach (ShoppingCartItem cartdata in cartList)
            {
                if(cartdata.Product.ProductCategories.Any())
                if (cartdata.Product.ProductCategories.FirstOrDefault().Category.Name == category)
                {
                    count++;
                }
            }

            if (count > 1)
            {
                return true;
            }
            return false;
        }

     bool Alternate_Discount_System(string category)
        {
            if (category == "Actuators" ||
                category == "AIDC/ATDC Sensors" ||
                category == "Bearings/Bushings" ||
                category == "Clutches" ||
                category == "Fans" ||
                category == "Filters" ||
                category == "Fusing Unit Parts" ||
                category == "Gears" ||
                category == "Latches" ||
                category == "Memory" ||
                category == "Motors" ||
                category == "Paper Feed" ||
                category == "Photo Interrupters" ||
                category == "Scan Units" ||
                category == "Separation Fingers" ||
                category == "Springs" ||
                category == "Staples" ||
                category == "Switches")
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        decimal Discount_Price(string category, IList<ShoppingCartItem>  cartList)
        {           
            int count = 0;

            foreach (var cartdata in cartList)
            {
                var itemCategory = string.Empty;
                if (cartdata.Product.ProductCategories.FirstOrDefault() != null)
                    itemCategory = cartdata.Product.ProductCategories.FirstOrDefault().Category.Name;

                if (itemCategory == category)
                {
                    count += cartdata.Quantity;
                }
            }

            if (count < 2)
            {
                return (1);
            }
            else if (count == 2)
            {
                return (0.58M);
            }
            else if (count == 3)
            {
                return (0.425M);
            }
            else if (count == 4)
            {
                return (0.353M);
            }
            else if (count == 5)
            {
                return (0.31M);
            }
            else if (count == 6)
            {
                return (0.28M);
            }
            else if (count == 7)
            {
                return (0.26M);
            }
            else if (count == 8)
            {
                return (0.253M);
            }
            else if (count == 9)
            {
                return (0.245M);
            }
            else if (count == 10)
            {
                return (0.24M);
            }
            else if (count == 11)
            {
                return (0.235M);
            }
            else
            {
                return (0.233M);
            }
        }


        /// <summary>
        /// Gets available pickup points
        /// </summary>
        /// <param name="address">Address</param>
        /// <param name="customer">Load records allowed only to a specified customer; pass null to ignore ACL permissions</param>
        /// <param name="providerSystemName">Filter by provider identifier; null to load pickup points of all providers</param>
        /// <param name="storeId">Load records allowed only in a specified store; pass 0 to load all records</param>
        /// <returns>Pickup points</returns>
        public virtual GetPickupPointsResponse GetPickupPoints(Address address, Customer customer = null, string providerSystemName = null, int storeId = 0)
        {
            var result = new GetPickupPointsResponse();
            var pickupPointsProviders = LoadActivePickupPointProviders(customer, storeId);

            if (!string.IsNullOrEmpty(providerSystemName))
            {
                pickupPointsProviders = pickupPointsProviders
                    .Where(x => x.PluginDescriptor.SystemName.Equals(providerSystemName, StringComparison.InvariantCultureIgnoreCase)).ToList();
            }

            if (pickupPointsProviders.Count == 0)
                return result;

            var allPickupPoints = new List<PickupPoint>();
            foreach (var provider in pickupPointsProviders)
            {
                var pickPointsResponse = provider.GetPickupPoints(address);
                if (pickPointsResponse.Success)
                    allPickupPoints.AddRange(pickPointsResponse.PickupPoints);
                else
                {
                    foreach (var error in pickPointsResponse.Errors)
                    {
                        result.AddError(error);
                        _logger.Warning($"PickupPoints ({provider.PluginDescriptor.FriendlyName}). {error}");
                    }
                }
            }

            //any pickup points is enough
            if (allPickupPoints.Count > 0)
            {
                result.Errors.Clear();
                result.PickupPoints = allPickupPoints.OrderBy(point => point.DisplayOrder).ThenBy(point => point.Name).ToList();
            }

            return result;
        }

        #endregion

        #endregion
    }
}
