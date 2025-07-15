# XR 5.0 TrainingProgram Repository


## Description
Prototype storage/sharing module for the XR5.0 TrainingProgram platform. Tenant is used for file storage, and a web app developed in C# provides an interface between the storage and the rest of the platform. This is a containerized version.

## Installation
This is a dockerized version and requires docker to be already installed
- Clone this repository: `git clone https://github.com/xr50-syn/XR5.0-training-asset-repository.git .`

- Edit the `.env` file to change the `XR50_OWNCLOUD_TRUSTED_DOMAINS` variable. This variable should contain all possible hostnames/IP addresses of the Tenant installation. For example, this line on our test server looks like this:
 `XR50_OWNCLOUD_TRUSTED_DOMAINS=localhost,tenant,amethyst,192.168.190.33,amy.library.lab.synelixis.com`
<br> The two first entries (localhost and own cloud) should always be present, while the rest(`amethyst`, `192.168.190.33`, and `amy.library.lab.synelixis.com`) are the specific installation IP addresses and DNS names.
In this file, you can also modify the admin username/password for the Tenant admin user, the Tenant Database user name&password and the XR5.0 DB username&password 

- Build the containers by using 
`docker-compose --profile lab up --build`

- Verify that the components have started properly by  opening a browser and connecting to `localhost:8080` (tenant installation) and `localhost:5286/swagger`(Repo swagger interface)

- To stop the containers use 
`docker-compose --profile lab down`
## Usage

## Support
emaurog@synelixis.com

## Roadmap
Update functionality based on suggestions from partners


## License
MIT License

