# Synaptrix8.1

<p align="center"\>  
 <img width="99" height="99" alt="Synaptrix8.1" src="https://github.com/user-attachments/assets/ff77849c-da96-4935-9f65-0ffcba0dfdaa" />
</p\>

I started this project to find a way to use facebook messenger on windows phone devices. While not the most trivial it is functional and I tried my best in making it easy to follow and use. Now in contrast to my UWP project I extended it to be used with all the mautrix GO based bridges. At the core is Synapse, a matrix homeserver. And with the use of the mautrix bridges we can communicate through a wide variety of services!

The written guide for the server setup is mostly finished and is available [here](https://github.com/ElisaC04/Synaptrix8.1/edit/main/SETUP.md). It covers creating the Debian VM, your network, configuring Synapse and the mautrix-meta bridge. I plan on adding the full config for the rest of the bridges but for now everything is linked in the guide and its not so difficult to follow.

The app will feel most buggy when syncing, especially if you are in a VM it might take time for all the requests to go through. Still lots of the syncing bugginess is thanks to my coding, I will slowly smooth everything out. Id say right now its at a usable state :)
