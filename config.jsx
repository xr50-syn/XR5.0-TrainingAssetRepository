//HOLO

//import tenant from  "./config";
//const tenant = localStorage.getItem("tenant");

//END HOLO

// Set VITE_SERVER_API (env var or .env file) to point the app at another host.
// The docker-compose sandbox profile in the training-repo sets it to http://localhost.
export const serverAPI = import.meta.env.VITE_SERVER_API || 'http://192.168.10.200';

let tenant = "test-company";
let bucket = "xr50-sandbox-tenant-pilot1";


try {
  tenant = localStorage.getItem("tenant") || tenant;

  const response = await fetch(`${serverAPI}:5286/xr50/trainingAssetRepository/Tenants/${tenant}`);

  const data = await response.json();

  if (!response.ok) {
    localStorage.setItem('tenant', '');
    window.location.reload();
  }

  bucket = data?.s3Config?.bucketName;

} catch (error) {
  console.error("Error fetching data:", error);
  alert('An error occurred while fetching data.');
}


const config = {
  xrAPI: `${serverAPI}:5286/api/${tenant}`,
  xrAPIBucket: `${serverAPI}:10000/${bucket}/${tenant}`,
  xrAPIAI: `${serverAPI}:5286/api/${tenant}/Chat/ask`
};

export default config;
