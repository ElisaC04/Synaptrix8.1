# Synapse homeserver setup guide

  

<p align="center"\>  
 <img width="99" height="99" alt="Synaptrix8.1" src="https://github.com/user-attachments/assets/ff77849c-da96-4935-9f65-0ffcba0dfdaa" />
</p\>

# Table of contents
- [Requirements](#req)
- [The brief architecture](#arch)
- [Risks](#risks)
- [Setting up Debian](#sdeb)
  - [Create the virtual machine](#vm)
  - [Installing Debian](#ideb)
  - [Installing dependencies](#dep)
- [Network and DynamicDNS](#net)
  - [Port forwarding](#pf)
  - [DynamicDNS](#dns)
- [Install/configure Synapse, Postgre, nginx and UFW](#combined)
  - [PostgreSQL](#sql)
  - [Synapse](#synapse)
  - [nginx](#nginx)
  - [UFW](#ufw)
- [Configuring the bridges](#bridges)
  - [Systemd service](#systemd)
- [Configure the bridge bot](#bot)
- [Synaptrix8.1](#synaptrix)

## Requirements <a name="req" id="req"></a>

- A linux (virtual)machine  
I configured my server on Debian so I wrote this guide for it. While the setup shouldn’t differ for similar distributions, I recommend using Debian.
I don’t see why WSL couldn’t work but you should do your own research if thats the route you would take.

- Administrator access to your router (or routers if for example you have one for yourself, but its not the router that your Internet Service Provider connects to)  

- A registered domain (We will get one for free with DuckDNS)

- Patience

## The brief architecture <a name="arch" id="arch"></a>

The main idea is that instead of directly talking to the facebook, google, telegram etc. servers from our Windows Phone we have our Debian machine take care of the communication. So from your phone you will connect to your own server that will have every chat and contact (since it downloads it from the official servers) and every message you send first gets sent to your server, and in your name it sends it to the official servers. Of course from your perspective you only see an ordinary chat room.

The reason you have to configure your own server is because this machine will be handling lots of sensitive data. This way only you have access to your server, so your data remains in your hands.

The main piece of software is Synapse, a matrix homeserver. We will have to configure PostgreSQL databases for Synapse and our bridges. The bridges are what connects us to the third party servers. For each service you want to use (messenger, telegram, discord etc.) you will have to install and configure its own bridge. Luckily these are very streamlined and the installation is the same for all (GO based) bridges, the only thing they differ in is the way you manage to get the bridge to login in your name.

In the Synaptrix8.1 app all you will have to do is login and it will sort every message into its own tab, depending on what service it came from. But you can also login to your server from any other matrix client (for example Element (classic) ) so not only can you utilize it from your Windows Phone but any other device you can get a matrix client on.

## Risks <a name="risks" id="risks"></a>


You have to be aware of the fact that in some way or form you are interacting with official services in a way in which they did not intend. It is annoying but it is a fact. So we have to be careful while setting up parameters. For example if you configure your mautrix-meta (messenger) bridge to try and download EVERY chat history at once every couple of seconds there is a very high chance your account gets flagged and banned.

  

Other things to consider with for example mautrix-telegram is if your account is relatively new and they notice you are logging in through these not so standard methods there is a high chance you will get flagged as a bot and get banned.

  

But by default every configuration regarding downloading chat history is kept at a sensible rate, so unless you modify them you should be alright. I have been running my instance for over a month and nothing bad has happened yet.

  

I wont go over every bridge setup, but I will tell you where you find them and every important information regarding said bridge will be there.

  

Another downgrade, and essentially a security risk you are taking is the loss of End 2 End Encryption. Essentially every modern chat service has implemented it, and while there exists a matrix client for Windows Phone 10 that supports E2EE, Windows phone 8.1 and down does not. The communication between the chat client and your server goes through HTTPS. Otherwise Synapse does support E2BE (End 2 Bridge Encryption) but we have to keep it turned off.

  

Is it less secure? Yes. Does this mean HTTPS is not secure? No. But its something you should have in mind, and ultimately its the price we have to pay for using old technology in a modern world.

  

**With this said, while I did my best in making this is accessible and secure as I could, everything you do is your responsibility. I am not responsible for any result that may come from you following this guide or using the application I developed.**

  

**I did this out of my love for this platform, to learn and of course for my enjoyment. :)**

# 1 Setting up Debian <a name="sdeb" id="sdeb"></a>

If you already have a machine running Debian make sure it has a static IP address. If its a VM make sure its network adapter is either bridged or if its NAT make sure TCP port 8443 is forwarded.

For this section I will be using VirtalBox, but you can use any preferred hypervisor.

1 **Enable VM support on your computer**

  

If you are running Windows/Linux you most likely have an option in your motherboard BIOS to enable

virtualization support (VT-x, AMD-v). Usually you get in by pressing DEL, F2 or F12 after powering

on your computer, but its the best to search online how to do so, for your specific machine or

motherboard online.  
If you are running MacOS you shouldn’t need to modify anything.

  

2 **Install VirtualBox**

  

 Visit https://www.virtualbox.org/wiki/Downloads and download the one appropriate for your system.

Go through the setup normally, there is no need to change anything.

  

3 **Download the Debian ISO**  

  

Go to the [debian website](https://www.debian.org/distrib/) and I recommend downloading the complete installation image, so “64-bit PC DVD-1 iso”.

  

### 4 **Create the virtual machine**  <a name="vm" id="vm"></a>

**4.1** Inside VirtualBox click on New, then name your machine and specify where the ISO you downloaded is. Uncheck “Proceed with Unattended Installation”

**4.2** Then click on Specify virtual hardware. A rule of thumb for virtual machines is never give more CPU cores or memory than half of what you have. This can always be changed later so if you can give it two CPU’s and 4GB (4096MB) of RAM. But while we are messing around setting it up feel free to give it more, as much as you can so our UI and everything goes as smooth as possible. Afterwards when you just boot it up for it to run in the background you can give it less.

**4.3** Then click on Specify virtual hard disk. Just to be safe Id give the machine minimum 50GB. Dont be afraid to give it more, unless you check ‘Pre-allocate Full Size’ it will only occupy as much space as it actually occupies.

**4.4** And click on Finish.

**4.5** Then go into the settings of the machine and navigate to the Network section. By default Adapter 1 is enabled and set to NAT. This means that the server isnt exposed to the network outside of your computer, instead it communicates through the address of your computer.
If you want to you can change NAT to Bridged mode. This will make the server connect “directly” to the router your computer is connected to, so it will receive an IP address from it.
If you keep it on NAT mode that means we have to configure Port Forwarding in Vbox so the server can be reached from the outside. To do so click on Port Forwarding and add a new rule with the add button in the top right. (This is what I recommend)

**4.6** Name it something like “Synapse Inbound”, keep the Protocol on TCP, Host IP blank, Host Port as 8443, Guest IP blank and finally Guest Port as 8443.

**4.7** And click on OK, then once again OK out of the settings and we can start the machine.

**4.8** Its a good time to also open up port 8443 in your operating systems firewall.  
In windows you can do that by opening up the Windows Defender Firewall and selecting advanced settings (or opening Windows Defender Firewall with Advanced Security).


**4.9** Click on Inbound rules, then add a new rule.


**4.10** Select a Port rule


**4.11** Then set it to be a TCP rule and for “specific local ports” type in 8443.


**4.12** Then leave “Allow the connection” checked.


**4.13** If your home network connection in windows is set to Private you can just leave Private checked and everything else unchecked. But you can keep all three options checked.

**4.14** Name the rule something like “Synapse inbound”.  
  
  
### 5 **Installing Debian** <a name="ideb" id="ideb"></a>
  
**5.1** With the Debian VM selected click on Start. It should boot up to a screen with a couple options, select Graphical install.  
  
**5.2** Then select your install language, location and keyboard layout.  
  
**5.3** After loading and configuring itself it should ask for a hostname. Name it whatever you like, I recommend something along the lines of “synapse-server”.

**5.4** Then it will ask for a domain name, if you have a running domain name in your local network thats what you should type here, if you don’t know about it you probably don’t have one configured and thats not an issue. In that case type in something like “home.arpa”.
This means that this server, if this same domain is configured in your router, can be reached locally through “synapse-server.home.arpa”.

**5.5** The setup will then ask you for the root user password. I recommend you leave everything blank so that the first user we create will become the root user (If you are unfamiliar with *nix terms, you could say root is the administrator account). Leaving the root account disabled is akin to disabling the Administrator account under windows.

**5.6** Next type in the full name of your user, it can be your real name or anything you’d like.

**5.7** Then the setup will suggest a username based on the real name, again this can be anything you want it to be.

**5.8** And now type in the password you will use to log onto your server.

**5.9** Time for the disk partitioning, if you have any preference of course go ahead but if you keep “Guided – use entire disk” selected you will be just fine.

**5.10** In the next screen just click continue.

**5.11** Then “All files in one partition” is good.

**5.12** And finally select Finish partitioning and write changes to disk, in the next screen select yes.

**5.13** The system is finally installing!

**5.14** At the “Configure the package manager” keep No checked and continue.

**5.15** At the next screen for using a network mirror select “yes”.

**5.16** Then select your country or the country closest to yours at “Debian archive mirror country”.

**5.16** And select whichever you want at “Debian archive mirror”.

**5.17** At the HTTP proxy most people will be good with leaving it blank.

**5.18** At the “Participate in the package user survey” select whichever you want.

**5.19** Next comes “Choose software to install”. Leave standard system utilities selected and we can also select a desktop environment. By default a DE, depending on which, will make the VM consume more resources. Since we only want it to run in the background and consume as little as possible it would make sense to choose none.
But luck has it that we can always disable it and log in to plain CLI so choose whichever you want, especially for someone new to linux it will make life easier.
The DE that consumes the least amount of resources in my experience is “Xfce”, so keep Debian Desktop Environment selected too and check XFCE, that will give us a nice DE.

**5.20** At the “Install the GRUB boot loader…” select yes.

**5.21** At the “Device for boot loader installation” select the line that starts with "/dev/sda” and continue.

**5.22** AND finally the system is installed, select continue to reboot.

**5.23** We will do most, essentially all of our work in the Terminal.

**5.24** Now we want to update our server for the first time, but to successfully run the commands we need to edit a file so that it doesn’t try to update from the installation ISO. This will be a good introduction to Nano, the terminal text editor I will be using in this guide.

**5.25** Open the terminal which you can find by searching for it in the software search bar.
Type in:

```
sudo nano /etc/sources.list
```

Running any command as sudo is akin to running something with administrator rights. Type in your user password and the file will open.
You can navigate the file with the arrow keys, delete the line that starts with “deb cdrom:” and save the file with ctrl+s, then exit Nano with ctrl+x.

**5.26** Then we can check for updates by typing:

```
sudo apt update
```

**5.27** And if it found anything to upgrade type:

```
sudo apt upgrade
```

*(To power off the machine you can type:*

```
sudo poweroff
```

*And to restart your machine you can type:*

```
sudo reboot
```
*)*

**5.28** Now we install VBox guest tools. This makes it possible for us to have a shared clipboard among many things. So if you copy something on your host machine you can paste it inside of the VM, and this makes this a whole lot easier:)
In the VM window at the top click on `Devices` and select `Insert Guest Additions CD Image`

**5.29** Install the dependencies by running

```
sudo apt install -y build-essential dkms linux-headers-$(uname -r)
```

**5.30** Create a mount directory

```
sudo mkdir -p /mnt/cdrom
```

**5.31** Mount the guest image

```
sudo mount /dev/cdrom /mnt/cdrom
```

**5.32** Go into the directory and run the setup

```
cd /mnt/cdrom
sudo ./VBoxLinuxAdditions.run
```

*If the installer errors out with something regarding the video driver just ignore it and wait until the script finishes*

**5.33** Restart Debian

```
sudo reboot
```

**With this the base for our server is good to go!**

### 6 **Installing dependencies** <a name="dep" id="dep"></a>

To make it simpler down the line we will install every dependency needed by the database, web server and certificate software in one go.

**6.11** Run this command inside your terminal:

```
sudo apt install -y curl wget gnupg2 lsb-release apt-transport-https ufw postgresql python3-psycopg2 nginx certbot jq ffmpeg
```

**6.12** When it prompts you type in your password and if you encounter a Y/N option type in Y .

---

# 7 Network and DynamicDNS <a name="net" id="net"></a>

You should be familiar with your routers model as every step involving the router will be unique to it, if you don’t know how you will have to search online.

7.1 **Setting a static IP**

Set a static local IP address for the computer that is running the VM, running Debian, or for the VM itself if you set the network mode to bridged. This is needed because when we configure port forwarding rules, those will be bound to an IP address. If the router or your computer restarts and you don’t get the same IP your server will not be reachable.

So we need to log into the routers web interface. If you are on windows you can open cmd, type in `ipconfig` and find an adapter that has a “Default Gateway” shown. In your browser type that address in to the URL bar and you should be greeted with some type of login screen. If you don’t know the login info ask anyone who might know for it before trying to reset. It might even be the default username and password combo which you can find out by searching for it on the web (also change it afterwards if this is the case).

Here is where all I can say is either search online or look around for the options to do so, be careful messing around with settings.

So if you configured NAT for your VM or you have a dedicated machine for Debian, you will want to set a static IP for the computer that the VM/Debian is running on, you will need that computers MAC address.

If you are using Bridged mode for your VM you will have to use the VM’s MAC address to set the IP.

### 8 **Port forwarding** <a name="pf" id="pf"></a>

Navigate to your routers port forwarding interface. Make the inside and outside port TCP 8443 and set the local IP/host to the static IP you set beforehand. Leave anything regarding outside addresses, connection sources etc. blank.
Now your server can be reached through your routers outside address, through the 8443 port. If this is the router that your ISP connects to then you are good to go here. If you have another router(s) in front of yours then you need to create this port forwarding rule on every single one, of course the local address being not the server this time, but the routers outside address that you last configured port
forwarding on.

At this point you can check if your entire port forward chain works via powershell on windows (Make sure Debian is connected and running). First find your public IP address, the easiest way is to open a site like https://whatismyipaddress.com/ and copy what it shows.
Then open powershell and type in this command where IP is your public IP:

```
tnc IP -Port 8443
```

If everything is good is should almost instantly say it succeeded.

### 9 **DynamicDNS** <a name="dns" id="dns"></a>

So this is great for us because it gives us consistent access from outside into our local network. Instead of having to keep track somehow of our public IP address we just register, in our case to DuckDNS and remember that domain, which will always keep track of our public IP.
For this to work we need a device to be online that can run the DuckDNS software to keep track and report the public IP. This can be essentially any device thats behind your router, even the router itself if it supports it. If your router does (check around in settings or check online) I recommend using it, as the router is always online and essentially its a task that we balanced over to the router.

If you are running the VM, for example you can also use the host system for the DuckDNS software.
But of course we can also use our Debian server, and thats what I will detail here.

**9.1** Create your DuckDNS account by going to https://www.duckdns.org/ and log in with your preferred method which you can see at the top of the site.

**9.2** Then the page should show the option to create a domain. In the textbox simply type in the domain you’d like (eg. test-net, my-network, woodpigeon)

**9.3** Now copy somewhere your token thats on top of the website and remember your domain, which will be DOMAIN.duckdns.org .

**9.4** Then we create a directory for DuckDNS

```
sudo mkdir -p /opt/duckdns
sudo nano /opt/duckdns/duck.sh
```

**9.5** Paste this into your newly created file where DOMAIN is your subdomain, so without the duckdns.org part (test-net.duckdns.org -> test-net ) and TOKEN is your token

```
#!/bin/bash
echo url="https://www.duckdns.org/update?domains=DOMAIN&token=TOKEN&ip=" | curl -k -o /var/log/duckdns.log -K -
```
		
**9.6** Modify the script persmissions

```
sudo chmod 700 /opt/duckdns/duck.sh
sudo chown root:root /opt/duckdns/ducks.sh
```
		
**9.7** Run it manually once and check if it works, it should output OK

```
sudo /opt/duckdns/duck.sh
cat /var/log/duckdns.log
```

**9.8** And then we make it run every 10 minutes by running this command

```
(sudo crontab -l 2>/dev/null; echo "*/10 * * * * /opt/duckdns/duck.sh >/dev/null 2>&1") | sudo crontab -
```

**9.9** Then we create the script to grab our certificate

```
sudo nano /etc/letsencrypt/duckdns-hook.sh
```

**9.10** Paste this into the file where MYDOMAIN is your subdomain and MYTOKEN is your token

```
#!/bin/bash
TOKEN="MYTOKEN"
DOMAIN="MYDOMAIN"
curl -s "https://www.duckdns.org/update?domains=$MYDOMAIN&token=$MYTOKEN&txt=$CERTBOT_VALIDATION"
sleep 30
```
	
**9.11** Modify the permissions on the file

```
sudo chmod 700 /etc/letsencrypt/duckdns-hook.sh
sudo chown root:root /etc/letsencrypt/duckdns-hook.sh
```

**9.12** And finally we request our certificate where DOMAIN is your FULL domain, so including duckdns.org

```
sudo certbot certonly --manual --preferred-challenges dns --manual-auth-hook /etc/letsencrypt/duckdns-hook.sh -d DOMAIN
```
	
# 10-13 Installing and configuring Synapse, PostgreSQL, nginx and firewall <a name="combined" id="combined"></a>

*The setup follows the official guides*

https://element-hq.github.io/synapse/latest/setup/installation.html

https://element-hq.github.io/synapse/latest/postgres.html

### 10 PostgreSQL <a name="sql"></a>

**10.1** Enter the PostreSQL prompt

```
sudo -u postgres psql
```

**10.2** Create the databse for Synapse, replace yourpassword with an actual secure password, avoid characters like \ or ? as they will cause errors later on

```
CREATE USER synapse WITH PASSWORD 'yourpassword';
CREATE DATABASE synapse OWNER synapse LC_COLLATE 'C' LC_CTYPE 'C' TEMPLATE template0;
```

*exit the SQL prompt by typing `\q`*

### 11 Synapse <a name="synapse" id="synapse"></a>

**11.1** Download the archive keyring

```
sudo wget -O /usr/share/keyrings/matrix-org-archive-keyring.gpg https://packages.matrix.org/debian/matrix-org-archive-keyring.gpg
```

**11.2** Add the repository

```
echo "deb [signed-by=/usr/share/keyrings/matrix-org-archive-keyring.gpg] https://packages.matrix.org/debian/ $(lsb_release -cs) main" | sudo tee /etc/apt/sources.list.d/matrix-org.list
```

**11.3** Install synapse

```
sudo apt install -y matrix-synapse-py3
```

**The installer will ask you for your Server Name, this is your FULL duckdns domain. If you mistype it its hard to correct later on**

**11.4** Run this command to generate a registration secret, copy the random string it creates

```
cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 64 | head -n 1
```

**11.5** Now we open the main config file

```
sudo nano /etc/matrix-synapse/homeserver.yaml
```

**11.6** Find the block that starts with **database:** and make it look like this, where securepassword is the password you set in the SQL prompt

```
database:
name: psycopg2
args:
	user: synapse
	password: securepassword
	database: synapse
	host: 127.0.0.1
	cp_min: 5
	cp_max: 10
```

**11.7** Now at the bottom of the file paste this line, where secret is the registration secret you created above then save and exit the file

```
registration_shared_secret: "secret"
```

**11.8** Now lets change the permissions of the file

```
sudo chown root:matrix-synapse /etc/matrix-synapse/homeserver.yaml
sudo chmod 640 /etc/matrix-synapse/homeserver.yaml
```

**11.9** And we start synapse

```
sudo systemctl restart matrix-synapse
```
	
**11.10** Now we create an admin account

```
register_new_matrix_user -c /etc/matrix-synapse/homeserver.yaml http://localhost:8008
```

*The username and password you type here are the ones you will use to log in from your device, so make sure the password is really secure*
*When asked if it should be an admin account type yes*

**11.11** And we add our public URL to the bottom of the file, where DOMAIN is your full duckdns domain

```
public_baseurl: "https://DOMAIN:8443/"
```

This is what your file should look like (the app_service_config_files line you will get to at the end of this document)

<img width="2080" height="1431" alt="homeserver.yaml" src="https://github.com/user-attachments/assets/d18a9641-0b8f-4878-a510-f00d062e9bca" />


### 12 nginx <a name="nginx" id="nginx"></a>

**12.1** Create the site configuration

```
sudo nano /etc/nginx/sites-available/matrix
```

**12.2** Paste this entire section into the newly created file, where DOMAIN is your full duckdns domain

```
server {
	listen 8443 ssl;
	listen [::]:8443 ssl;
	server_name DOMAIN;
	
	ssl_certificate /etc/letsencrypt/live/DOMAIN/fullchain.pem;
	ssl_certificate_key /etc/letsencrypt/live/DOMAIN/privkey.pem;
	
	ssl_protocols TLSv1.2 TLSv1.3;
	ssl_ciphers 'ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256:ECDHE-ECDSA-AES256-GCM-SHA384:ECDHE-RSA-AES256-GCM-SHA384:DHE-RSA-AES128-GCM-SHA256:DHE-RSA-AES256-GCM-SHA384';
	ssl_prefer_server_ciphers off;
	
	add_header X-Content-Type-Options nosniff always;
	add_header X-Frame-Options DENY always;
	add_header X-XSS-Protection "1; mode=block" always;
	
	location /_matrix {
		proxy_pass http://127.0.0.1:8008;
		proxy_set_header X-Forwarded-For $remote_addr;
		proxy_set_header X-Forwarded-Proto $scheme;
		proxy_set_header Host $host;
		client_max_body_size 50M;
	}

	location /_synapse/client {
		proxy_pass http://127.0.0.1:8008;
		proxy_set_header X-Forwarded-For $remote_addr;
		proxy_set_header X-Forwarded-Proto $scheme;
		proxy_set_header Host $host;
		client_max_body_size 50M;
	}
}
```

**12.3** Enable the site and test it

```
sudo ln -s /etc/nginx/sites-available/matrix /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl restart nginx
```
	
### 13 UFW firewall <a name="ufw" id="ufw"></a>

**13.1** Run these commands to enable port 8443 and 22 and deny anything else

```
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp comment "SSH"
sudo ufw allow 8443/tcp comment "Matrix Nginx Proxy"
sudo ufw enable
```

**13.2** Test if your server is reachable form the outside by visiting its site through mobile data or an outside network, where DOMAIN is your full duckdns domain

```
https://DOMAIN:8443/_matrix/client/versions
```

# 14 Configuring the bridges <a name="bridges" id="bridges"></a>

I will detail how to configure the messenger (mautrix-meta) bridge. But every single step is the same for all the GO based bridges, except the users and direcotries (because every birdge needs their own), the config file itself, and the systemd script at the end. So for example if you are installing mautrix-telegram replace every instance of `meta` to `telegram`.

The general GO based bridge config is found here
https://docs.mau.fi/bridges/go/setup.html
There will be a dropdown box where you can select the specific bridge youd like.

And the authentication for each bridge can be read by selecting it in the left side menu on the site. For example the mautrix-meta authentication can be read here
https://docs.mau.fi/bridges/go/meta/authentication.html


**14.1** Create a PostgreSQL user and database for the bridge where bridgepassword is an actual secure password

```
sudo -u postgres psql
```
```
CREATE USER mautrix_meta WITH PASSWORD 'bridgepassword';
CREATE DATABASE mautrix_meta OWNER mautrix_meta;
```
```
\q
```

**14.2** Create a system user for the bridge

```
sudo adduser --system --group --no-create-home --home /opt/mautrix-meta --shell /usr/sbin/nologin mautrix-meta
```

**14.3** Create the working directory

```
sudo mkdir -p /opt/mautrix-meta
cd /opt/mautrix-meta
```

**14.4** Give the bridge user ownership of the directory

```
sudo chown -R mautrix-meta:mautrix-meta /opt/mautrix-meta
sudo chmod 750 /opt/mautrix-meta
```

**14.5** Download the binary for the bridge and make it executable

*The latest version of mautrix-meta is available at https://github.com/mautrix/meta/releases/latest/*

*You can find each binary for the birdges at their respective github repo, for example by searching for mautrix-telegram github, mautrix-gmessages github ...*

```
sudo wget https://github.com/mautrix/meta/releases/latest/download/mautrix-meta-amd64
sudo chmod +x mautrix-meta-amd64
```

**14.6** Generate a sample config and open it

```
sudo -u mautrix-meta ./mautrix-meta-amd64 -generate
sudo -u mautrix-meta nano /opt/mautrix-meta/config.yaml
```

**14.7** Now we have to edit some sections of this file

By using `ctrl+f` you can search for words

Search for the **homeserver:** block and make sure the address and domain properties are set like this, where DOMAIN is your full duckdns domain

```
homeserver:
	address: http://127.0.0.1:8008
	domain: DOMAIN
```

Search for the **database:** block and make sure the type and uri properties are set like this, where bridgepassword is the password you set in SQL above, and bridgeuser is the user you created in SQL above

```
database:
	type: postgres
	uri: postgres://bridgeuser:bridgepassword@127.0.0.1:5432/mautrix_meta?sslmode=disable
```

Search for the **network:** block and add this new property so that it looks like this 
*This is only for mautrix-meta*

```
network:
	mode: messenger
```

Search for the **permissions:** block and make sure the user and admin user are set, where DOMAIN is your full duckdns domain and synapseuser is the username you use to login from your device

```
permissions:
	"*": relay
	"DOMAIN": user
	"@synapseuser:DOMAIN": admin
```
Search for the **backfill:** block and make sure it is enabled

```
backfill:
	enabled: true
```

**14.8** Now save and exit the file

**14.9** Now we generate the registration file

```
sudo -u mautrix-meta ./mautrix-meta-amd64 -generate
```

**14.10** And we copy it over to the synapse directory

```
sudo cp /opt/mautrix-meta/registration.yaml /etc/matrix-synapse/mautrix-meta.yaml
sudo chown matrix-synapse:matrix-synapse /etc/matrix-synapse/mautrix-meta.yaml
sudo chmod 640 /etc/matrix-synapse/mautrix-meta.yaml
```

**14.11** And now we update the synapse config

```
sudo nano /etc/matrix-synapse/homeserver.yaml
```

And we add this block at the bottom of the file. *If you are adding another config you only have to add in a new `- "/etc/matrix-synapse/mautrix-XXX.yaml"` under the existing one, make sure the new line is also indented

```
app_service_config_files:
	- "/etc/matrix-synapse/mautrix-meta.yaml"
```

**14.12** Then we set permissions

```
sudo chown -R mautrix-meta:mautrix-meta /opt/mautrix-meta
sudo chmod 750 /opt/mautrix-meta
sudo chmod 600 /opt/mautrix-meta/config.yaml
```

**14.13** And we restart synapse

```
sudo systemctl restart matrix-synapse
```

### Systemd service <a name="systemd" id="systemd"></a>

**15.1** Create a new systemd file

```
sudo nano /etc/systemd/system/mautrix-meta.service
```

**15.2** Paste this entire section into the file

```
[Unit]
Description=Mautrix-Meta Bridge
After=network.target matrix-synapse.service postgresql.service
Wants=matrix-synapse.service postgresql.service

[Service]
Type=simple
User=mautrix-meta
Group=mautrix-meta
WorkingDirectory=/opt/mautrix-meta
ExecStart=/opt/mautrix-meta/mautrix-meta-amd64
Restart=always
RestartSec=5s
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=true
PrivateTmp=true
ProtectKernelTunables=true
ProtectControlGroups=true
ReadWritePaths=/opt/mautrix-meta

[Install]
WantedBy=multi-user.target
```

**15.3** Enable and start the bridge

```
sudo systemctl daemon-reload
sudo systemctl enable --now mautrix-meta
```

# Configuring the messenger bridge bot <a name="bot" id="bot"></a>

For this section I highly recommend you use the Element app on your desktop, but you can use it on Andoird/iOS too, just to make sure it goes smoothly.

*Check the authentication section of the bridge you are configuring*

**16.1** Connect to your server via Element, at the server field input this, where DOMAIN is your full duckdns domain

`https://DOMAIN:8443/`

**16.2** Login with your username and password you configured

**16.3** Locate the start chat button and search for this user, where DOMAIN is your full duckdns domain

`@facebookbot:DOMAIN`

**16.4** The user should appear under suggested users, tap on it

If you type in something it should reply, confirming its working

**16.5** This section I recommend doing on a desktop browser

**16.6** Open https://messenger.com and make sure you are logged in. Press F12 to open DevTools and open the Network tab.

**16.7** Refresh the website and at the very top of the DevTools network box there should be an item called “messenger.com”

**16.8** Select it, right click it and select “copy as cURL”

**16.9** And send exactly what you copied without modifying it to the facebookbot.

And you should instantly get flooded by invites! Each invite corresponds to a group chat or 1 on 1 chat. Accepting the invite is what allows you to enter the chat and send/recieve messages.

*You wont be able to see your entire past message history. It will mostly show from the point the bridge first synced with your facebook account. But the backlogging will periodically download message history.*

# Synaptrix8.1 <a name="synaptrix" id="synaptrix"></a>

And now all you have to do is download the app onto your Windows Phone 8.1 or 10. You can get the latest appxbundle from the [releases](https://github.com/ElisaC04/Synaptrix8.1/releases) page!

You will login the exact way you did into Element. One quirk of this setup is that messages sent by you on other platforms (for example the official messenger app) by default will show as they are a separate user in chats. Because in the Synapse databse they are! So to make every message sent by **you** show up as sent by you inside your chats I added a feature in the settings menu where you can add your specific bridge ID's.

In the case of messenger you have to open the Element app, open a chat and find a message sent by your facebook user and click on the profile picture. There you will see its metaID, copy just the numbers noting more or less. And this is what you paste in the metaID field, if done properly you will see every message sent
by your facebook user as actually sent by you!

