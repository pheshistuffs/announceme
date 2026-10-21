plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.announceme"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.announceme"
        minSdk = 26
        targetSdk = 35
        versionCode = (System.getenv("GITHUB_RUN_NUMBER") ?: "1").toInt()
        versionName = "1.0.${System.getenv("GITHUB_RUN_NUMBER") ?: "0"}"
    }

    // The interface is shared with the Windows app.
    sourceSets["main"].assets.srcDir("../../ui")

    // A fixed key kept in the repository, so every build installs over the previous one.
    // It only identifies this hobby app; replace it before publishing anywhere.
    signingConfigs {
        create("app") {
            storeFile = file("announceme.keystore")
            storePassword = "announceme"
            keyAlias = "announceme"
            keyPassword = "announceme"
        }
    }

    buildTypes {
        release {
            isMinifyEnabled = false
            signingConfig = signingConfigs.getByName("app")
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}
