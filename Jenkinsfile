pipeline {
	agent none
	stages {
		stage('Initialisation') {
			agent { node { label 'windows-node-3' }}
			steps {
				dir('') {
					configFileProvider([configFile(fileId: '70b23fed-a083-4c12-84a8-17d03a111512', targetLocation: 'versions-cc', )]) {
						script {
							def xml = readFile "${WORKSPACE}/versions-cc"
							def versions = new XmlSlurper().parseText(xml)
							env.GIT_BRANCH_CLEARED = "${GIT_BRANCH}".replace("origin/", "")
							echo "Branch name used for getting version number = '${GIT_BRANCH_CLEARED}'"
							env.version_major = versions.ugcs_video_transmitter."${GIT_BRANCH_CLEARED}".text()
							env.version_build = "${BUILD_NUMBER}"
							env.version = "${env.version_major}" + "." + "${env.version_build}"
							env.git_branch_ugcs_video_transmitter = "${GIT_BRANCH}"
							env.git_commit_ugcs_video_transmitter = "${GIT_COMMIT}"
						}
					}
					echo "Component version for branch = '${GIT_BRANCH}' is '${env.version_major}'"
				}
			}
		}
		stage('Preparation windows-node-3') {
			agent { node { label 'windows-node-3' }}
			steps {
				dir('') {
					sh 'rm -rf build && mkdir build'
					sh '''echo "version=${version}
git_branch_ugcs_video_transmitter=${git_branch_ugcs_video_transmitter}
git_commit_ugcs_video_transmitter=${git_commit_ugcs_video_transmitter} " > build/version.ini '''

					cifsPublisher alwaysPublishFromMaster: false, continueOnError: false, failOnError: false, publishers: [[
						configName: 'binaries_repo', transfers: [[
							cleanRemote: true, excludes: '', 
							flatten: false, makeEmptyDirs: false, 
							noDefaultExcludes: false, patternSeparator: '[, ]+', 
							remoteDirectory: "ugcs-video-transmitter/${version_major}/${version}", remoteDirectorySDF: false,
							removePrefix: 'build', 
							sourceFiles: 'build/version.ini']], 
						usePromotionTimestamp: false, 
						useWorkspaceInPromotion: false, 
						verbose: true
					]]
					cifsPublisher alwaysPublishFromMaster: false, continueOnError: false, failOnError: false, publishers: [[
						configName: 'binaries_repo', transfers: [[
							cleanRemote: true, excludes: '', 
							flatten: false, makeEmptyDirs: false, 
							noDefaultExcludes: false, patternSeparator: '[, ]+', 
							remoteDirectory: "ugcs-video-transmitter/${version_major}/latest", remoteDirectorySDF: false, 
							removePrefix: 'build', 
							sourceFiles: 'build/version.ini']], 
						usePromotionTimestamp: false, 
						useWorkspaceInPromotion: false, 
						verbose: true
					]]
				}
			}
		}
		stage('Building & Publishing: Win library') {
			agent { node { label 'windows-node-3' }}
			steps {
				bat ''' echo "build" '''

				cifsPublisher alwaysPublishFromMaster: false, continueOnError: false, failOnError: false, publishers: [[
					configName: 'binaries_repo', transfers: [[
						cleanRemote: false, excludes: '', 
						flatten: false, makeEmptyDirs: false, 
						noDefaultExcludes: false, patternSeparator: '[, ]+', 
						remoteDirectory: "ugcs-video-transmitter/${version_major}/${version}", remoteDirectorySDF: false,
						removePrefix: 'build', 
						sourceFiles: 'build/*.zip']], 
					usePromotionTimestamp: false, 
					useWorkspaceInPromotion: false, 
					verbose: true
				]]
				cifsPublisher alwaysPublishFromMaster: false, continueOnError: false, failOnError: false, publishers: [[
					configName: 'binaries_repo', transfers: [[
						cleanRemote: false, excludes: '', 
						flatten: false, makeEmptyDirs: false, 
						noDefaultExcludes: false, patternSeparator: '[, ]+', 
						remoteDirectory: "ugcs-video-transmitter/${version_major}/latest", remoteDirectorySDF: false, 
						removePrefix: 'build', 
						sourceFiles: 'build/*.zip']], 
					usePromotionTimestamp: false, 
					useWorkspaceInPromotion: false, 
					verbose: true
				]]
			}
		}
	}
	post { 
		always {
			slackSend message: "Build UgCS-CC video-transmitter ${version} - ${currentBuild.result}. (<${env.BUILD_URL}|Open>)"
		}
		success { notifyBuild('SUCCESSFUL') }
		failure { notifyBuild('FAILED') }
		aborted { notifyBuild('FAILED') }
	}
	options {
		buildDiscarder(logRotator(numToKeepStr:'10'))
		timeout(time: 30, unit: 'MINUTES')
	}
}

def notifyBuild(String buildStatus) {
	buildStatus =  buildStatus ?: 'SUCCESSFUL'

	def subject = "Build UgCS-CC video-transmitter ${version} - ${buildStatus}."
	def summary = "${subject} (${env.BUILD_URL})"
	def details = """
<html>
	<body>
		<article>
			<h3>Build UgCS-CC video-transmitter ${version} - ${buildStatus}.</h3>
		</article>
		<article>
			<h3>Summary</h3>
			<p>
				<table>
					<col width="60">
					<col width="300">
					<tr>
						<td>Git branch name</td>
						<td>
							<a href="https://bitbucket.org/sphengineering/ugcs-video-transmitter/branch/${git_branch_ugcs_video_transmitter}">${git_branch_ugcs_video_transmitter}</a>
						</td>
					</tr>
					<tr>
						<td>Git revision</td>
						<td>
							<a href="https://bitbucket.org/sphengineering/ugcs-video-transmitter.git/commits/${git_commit_ugcs_video_transmitter}">${git_commit_ugcs_video_transmitter}</a>
						</td>
					</tr>
					<tr>
						<td>Build logs</td>
						<td><a href="${env.BUILD_URL}">check build logs</a></td>
					</tr>
					<tr>
						<td>Synplicity path</td>
						<td>"Binaries/UgCS-CC/ugcs-video-transmitter/${version_major}"</td>
					</tr>
				</table>
			</p>
		</article>
		<article>
			<h3>Changelogs</h3>
			<p>${changeString}</p>
		</article>
	</body> 
</html>"""

	bitbucketStatusNotify(buildState: buildStatus )
	emailext (
		subject: subject, mimeType: 'text/html', body: details,
		to: 'ugcs_dev_cc@googlegroups.com'
	)
}


def getChangeString() {
	MAX_MSG_LEN = 100
	def changeString = ""
	echo "Gathering SCM changes"
	def changeLogSets = currentBuild.rawBuild.changeSets
	for (int i = 0; i < changeLogSets.size(); i++) {
		def entries = changeLogSets[i].items
		for (int j = 0; j < entries.length; j++) {
			def entry = entries[j]
			changeString += "<p><b>${entry.msg}</b></p>\n<p>${entry.author}: <a href='https://bitbucket.org/sphengineering/ugcs-video-transmitter.git/commits/${entry.commitId}'>${entry.commitId}</a></p>\n<p></p>\n"
		}
	}
	if (!changeString) {
		changeString = " No new changes"
	}
	return changeString
} 