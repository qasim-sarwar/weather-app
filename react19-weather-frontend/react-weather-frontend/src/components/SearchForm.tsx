type Props = {
  city: string;
  setCity: (v: string) => void;
  latitude: string;
  setLatitude: (v: string) => void;
  longitude: string;
  setLongitude: (v: string) => void;
  onSearchByCity: () => Promise<void>;
  onSearchByLatLon: () => Promise<void>;
  isLoading: boolean;
};

export default function SearchForm({
  city, setCity, latitude, setLatitude, longitude, setLongitude,
  onSearchByCity, onSearchByLatLon, isLoading
}: Props) {
  return (
    <div className="weather-form-row">
      <div className="weather-form-section">
        <form onSubmit={(e) => { e.preventDefault(); onSearchByCity(); }} className="weather-form" autoComplete="off">
          <div className="input-row">
            <input
              type="text"
              className="weather-input"
              placeholder="City Name"
              value={city}
              onChange={(e) => setCity(e.target.value)}
            />
          </div>
          <button type="submit" disabled={isLoading} className="weather-btn">
            {isLoading ? 'Loading...' : 'Fetch by City'}
          </button>
        </form>
      </div>

      <div className="weather-divider" />

      <div className="weather-form-section">
        <div className="input-row">
          <input
            type="number"
            className="weather-input"
            placeholder="Latitude"
            step="any"
            value={latitude}
            onChange={(e) => setLatitude(e.target.value)}
          />
          <input
            type="number"
            className="weather-input"
            placeholder="Longitude"
            step="any"
            value={longitude}
            onChange={(e) => setLongitude(e.target.value)}
          />
        </div>
        <button onClick={onSearchByLatLon} disabled={isLoading} className="weather-btn">
          {isLoading ? 'Loading...' : 'Fetch by Latitude/Longitude'}
        </button>
      </div>
    </div>
  );
}
