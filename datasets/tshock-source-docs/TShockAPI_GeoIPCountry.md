# GeoIPCountry.cs

**命名空间**: `MaxMind`

## 类型定义
- **GeoIPCountry** : `I`
- **is**
- **is**

## 方法
### `public GeoIPCountry(Stream datafile)`
The stream is not closed when this class is disposed. Be sure to clean up afterwards!

### `public GeoIPCountry(string filename)`
The file will be closed when this class is disposed.

### `string GetCountryCode(IPAddress ip)`
The IP address must be IPv4.

### `string TryGetCountryCode(IPAddress ip)`
Two-letter country code or null on failure.

### `static string GetCountryNameByCode(string countrycode)`
English name of the country, or null on failure.

### `int FindIndex(IPAddress ip)`

### `Converts an IPv4 address into a long, for reading from geo database
		private long AddressToLong(IPAddress ip)`

### `on the IP address mask
		private long FindCountryCode(long offset, long ipnum, int depth)`

### `void Dispose()`
